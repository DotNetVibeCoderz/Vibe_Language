using CSJS;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Data;

/// <summary>A note as it is stored in IndexedDB. The id is assigned by the store.</summary>
[JsObjectLiteral]
public class Note
{
    [JsName("id")] public int? Id { get; set; }
    [JsName("text")] public string Text { get; set; } = "";
    [JsName("created")] public double Created { get; set; }
}

/// <summary>
/// IndexedDB through Tasks. IDB reports results through <c>onsuccess</c> and <c>onerror</c> on a
/// request; wrapping each request in a Promise once lets every caller simply <c>await</c> it.
/// </summary>
public static class Idb
{
    // Task<T> is a Promise in JavaScript, so a Promise built from callbacks is a Task to C#.
    [JsTemplate("new Promise({0})")]
    private static Task<T> FromCallbacks<T>(Action<Action<T>, Action<object>> executor) => throw new JsExternException();

    /// <summary>Completes with the request's result, or faults with its error.</summary>
    public static Task<T> Await<T>(IDBRequest<T> request) => FromCallbacks<T>((resolve, reject) =>
    {
        request.On<Event>("success", _ => resolve(request.Result));
        request.On<Event>("error", _ => reject(request.Error!));
    });

    /// <summary>Opens a database, creating its stores on first use or after a version bump.</summary>
    public static Task<IDBDatabase> Open(string name, int version, Action<IDBDatabase> upgrade)
    {
        var request = Js.IndexedDB.Open(name, version);
        request.On<IDBVersionChangeEvent>("upgradeneeded", _ => upgrade(request.Result));
        return Await(request);
    }

    /// <summary>Completes when a transaction has committed — the moment its writes are durable.</summary>
    public static Task<bool> Done(IDBTransaction transaction) => FromCallbacks<bool>((resolve, reject) =>
    {
        transaction.On<Event>("complete", _ => resolve(true));
        transaction.On<Event>("error", _ => reject(transaction.Error!));
        transaction.On<Event>("abort", _ => reject(transaction.Error!));
    });
}

/// <summary>
/// The three places a page can keep data between visits: a notes store in IndexedDB, small
/// values in localStorage, and the quota both of them draw on.
/// </summary>
public sealed class Storage : Demo
{
    public override string Id => "storage";
    public override string Title => "Storage";
    public override string Category => "Data";
    public override string Summary => "A notes store in IndexedDB with every request awaited as a Task, a visit counter and an autosaved draft in localStorage, and the origin's quota from navigator.storage.estimate.";
    public override string[] Apis => new[] { "IndexedDB", "localStorage", "StorageManager.estimate", "StorageManager.persist" };
    public override string Module => "Demos/Data/Storage";
    public override string Hint => "Add a note, then reload the page — it is still there.";

    private const string Database = "csjs-gallery";
    private const string Store = "notes";
    private const string DraftKey = "csjs-gallery.storage.draft";
    private const string VisitsKey = "csjs-gallery.storage.visits";
    private const string LastVisitKey = "csjs-gallery.storage.last-visit";

    private IDBDatabase? _db;
    private HTMLInputElement _input = null!;
    private HTMLElement _notes = null!;
    private HTMLElement _noteCount = null!;
    private HTMLElement _quotaBar = null!;
    private HTMLElement _usage = null!;
    private HTMLElement _quota = null!;
    private HTMLElement _persisted = null!;
    private HTMLElement _local = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        var left = stage.Add(Ui.El("div", "panel"));
        var head = left.Add(Ui.El("div", "storage-head"));
        head.Add(Ui.El("h3", "panel-title", "IndexedDB · notes"));
        _noteCount = head.Add(Ui.El("span", "storage-count", ""));

        var form = left.Add(Dom.Create<HTMLFormElement>("form"));
        form.ClassName = "row";
        _input = form.Add(Ui.Input("Write a note to keep"));
        form.Add(Ui.Button("Save note", () => { }, "btn btn-primary")).Type = "submit";
        form.On<SubmitEvent>("submit", e =>
        {
            e.PreventDefault();
            AddNote();
        });
        _notes = left.Add(Ui.El("ul", "storage-notes"));

        var right = stage.Add(Ui.El("div", "panel panel-log storage-side"));
        right.Add(Ui.El("h3", "panel-title", "localStorage"));
        _local = right.Add(Ui.El("dl", "facts"));

        right.Add(Ui.El("h3", "panel-title storage-gap", "Quota for this origin"));
        var meter = right.Add(Ui.El("div", "storage-meter"));
        _quotaBar = meter.Add(Ui.El("i"));
        var numbers = right.Add(Ui.El("div", "row storage-numbers"));
        numbers.Add(Ui.Readout("Used", out _usage));
        numbers.Add(Ui.Readout("Available", out _quota));
        numbers.Add(Ui.Readout("Persistent", out _persisted));
        right.Add(Ui.Button("Ask to keep data persistent", () => Persist(), "btn btn-small"));
        right.Add(Ui.El("p", "storage-note",
            "Without persistence the browser may clear this origin's data when the disk runs low. With it, only you can."));

        Visits();
        Start();
    }

    public override void Unmount()
    {
        _db?.Close();
        _db = null;
    }

    /// <summary>A visit counter and the draft, both plain strings in localStorage.</summary>
    private void Visits()
    {
        var local = Js.LocalStorage;
        var visits = int.TryParse(local.GetItem(VisitsKey), out var count) ? count + 1 : 1;
        var last = local.GetItem(LastVisitKey);
        local.SetItem(VisitsKey, visits.ToString());
        local.SetItem(LastVisitKey, DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

        Controls.Fact(_local, "Visits to this demo", visits.ToString());
        Controls.Fact(_local, "Previous visit", last ?? "this is the first");
        var draft = Controls.Fact(_local, "Draft", "");

        // The note being typed survives a reload: every keystroke is saved, synchronously.
        _input.Value = local.GetItem(DraftKey) ?? "";
        draft.TextContent = _input.Value.Length > 0 ? "restored" : "empty";
        _input.On<Event>("input", _ =>
        {
            local.SetItem(DraftKey, _input.Value);
            draft.TextContent = $"saved · {_input.Value.Length} chars";
        });
    }

    private async Task Start()
    {
        if (!Controls.Supports(Js.Window, "indexedDB"))
        {
            _notes.Add(Ui.El("li", "empty", "This browser has no IndexedDB, or it is disabled in this private window."));
            return;
        }

        try
        {
            _db = await Idb.Open(Database, 1, db =>
            {
                if (!db.ObjectStoreNames.Contains(Store))
                    db.CreateObjectStore(Store, new IDBObjectStoreParameters { KeyPath = "id", AutoIncrement = true });
            });
            await Render();
        }
        catch (Exception error)
        {
            _notes.InnerHTML = "";
            _notes.Add(Controls.Notice("IndexedDB could not open", $"{error.Message} Private windows in some browsers block it.", "warn"));
        }

        await Estimate();
    }

    private async Task AddNote()
    {
        var text = _input.Value.Trim();
        if (text.Length == 0 || _db is null) return;

        var transaction = _db.Transaction(Store, "readwrite");
        var note = new Note { Text = text, Created = Now() };
        transaction.ObjectStore(Store).Add(note);
        await Idb.Done(transaction);

        _input.Value = "";
        Js.LocalStorage.RemoveItem(DraftKey);
        await Render();
        await Estimate();
    }

    private async Task Delete(int id)
    {
        if (_db is null) return;
        var transaction = _db.Transaction(Store, "readwrite");
        transaction.ObjectStore(Store).Delete(id);
        await Idb.Done(transaction);
        await Render();
        await Estimate();
    }

    private async Task Render()
    {
        if (_db is null) return;
        var store = _db.Transaction(Store, "readonly").ObjectStore(Store);
        var all = await Idb.Await(store.GetAll());
        var notes = all.Select(n => n.As<Note>()).OrderByDescending(n => n.Created).ToList();

        _noteCount.TextContent = notes.Count == 1 ? "1 note" : $"{notes.Count} notes";
        _notes.InnerHTML = "";
        if (notes.Count == 0)
        {
            _notes.Add(Ui.El("li", "empty", "No notes yet. Saved notes stay after a reload — and after closing the browser."));
            return;
        }

        foreach (var note in notes)
        {
            var row = _notes.Add(Ui.El("li", "storage-note-row"));
            row.Add(Ui.El("span", "storage-id", $"#{note.Id}"));
            row.Add(Ui.El("span", "storage-text", note.Text));
            row.Add(Ui.El("time", "todo-time", Ago(note.Created)));
            var id = note.Id ?? 0;
            row.Add(Ui.Button("Delete", () => Delete(id), "btn btn-ghost btn-small"));
        }
    }

    /// <summary>Milliseconds since the Unix epoch — a number IndexedDB stores and sorts as is.</summary>
    [JsTemplate("Date.now()")]
    private static double Now() => throw new JsExternException();

    private static string Ago(double created)
    {
        var seconds = (Now() - created) / 1000;
        if (seconds < 60) return "just now";
        if (seconds < 3600) return $"{Math.Floor(seconds / 60):F0} min ago";
        if (seconds < 86400) return $"{Math.Floor(seconds / 3600):F0} h ago";
        return $"{Math.Floor(seconds / 86400):F0} d ago";
    }

    private async Task Estimate()
    {
        if (!Controls.Supports(Js.Navigator, "storage"))
        {
            _usage.TextContent = "not reported";
            return;
        }

        var estimate = await Js.Navigator.Storage.Estimate();
        var usage = estimate.Usage ?? 0;
        var quota = estimate.Quota ?? 0;
        _usage.TextContent = Controls.Bytes(usage);
        _quota.TextContent = Controls.Bytes(quota);
        // A real share of the quota is tiny; the bar has a floor so it reads as "something".
        _quotaBar.Style.Width = quota > 0 ? $"{Math.Max(0.6, usage / quota * 100):F2}%" : "0";
        _persisted.TextContent = await Js.Navigator.Storage.Persisted() ? "yes" : "no";
    }

    private async Task Persist()
    {
        if (!Controls.Supports(Js.Navigator, "storage")) return;
        var granted = await Js.Navigator.Storage.Persist();
        _persisted.TextContent = granted ? "yes" : "declined";
    }
}
