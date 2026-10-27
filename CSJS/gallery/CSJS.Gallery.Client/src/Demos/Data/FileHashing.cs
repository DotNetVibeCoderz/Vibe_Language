using CSJS.Gallery.Client.Shell;
using CSJS.Web;
using File = CSJS.Web.File;

namespace CSJS.Gallery.Client.Demos.Data;

/// <summary>
/// Files dropped or picked, read entirely in the page: images previewed through object URLs,
/// text shown as text, and every file's SHA-256 computed by Web Crypto and printed as hex. No
/// byte leaves the browser.
/// </summary>
public sealed class FileHashing : Demo
{
    public override string Id => "files-and-hashing";
    public override string Title => "Files and hashing";
    public override string Category => "Data";
    public override string Summary => "Drag files in or pick them. Images preview through object URLs, text files show their opening lines, and each file gets a SHA-256 from crypto.subtle — all inside the page.";
    public override string[] Apis => new[] { "Drag and drop", "File", "URL.createObjectURL", "crypto.subtle.digest" };
    public override string Module => "Demos/Data/FileHashing";
    public override string Hint => "Drop a few files of different kinds onto the zone.";

    private HTMLElement _zone = null!;
    private HTMLInputElement _picker = null!;
    private HTMLElement _list = null!;
    private HTMLElement _count = null!;
    private HTMLElement _total = null!;
    private readonly List<string> _objectUrls = new();
    private double _bytes;
    private int _files;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("files-stage");

        _zone = stage.Add(Ui.El("div", "files-zone"));
        _zone.SetAttribute("tabindex", "0");
        _zone.SetAttribute("role", "button");
        _zone.SetAttribute("aria-label", "Drop files here, or press Enter to choose them");
        _zone.Add(Ui.El("span", "files-zone-mark", "↓"));
        _zone.Add(Ui.El("p", "files-zone-title", "Drop files here"));
        _zone.Add(Ui.El("p", "files-zone-body", "or click to choose them. Nothing is uploaded — the browser reads them locally."));

        _picker = stage.Add(Dom.Create<HTMLInputElement>("input"));
        _picker.Type = "file";
        _picker.Multiple = true;
        _picker.Hidden = true;
        _picker.On<Event>("change", _ =>
        {
            if (_picker.Files is { } files) Add(files);
            _picker.Value = "";
        });

        _zone.On<MouseEvent>("click", _ => _picker.Click());
        _zone.On<KeyboardEvent>("keydown", e =>
        {
            if (e.Key is "Enter" or " ") { e.PreventDefault(); _picker.Click(); }
        });

        // dragover must be cancelled, or the browser refuses the drop and opens the file instead.
        _zone.On<DragEvent>("dragover", e =>
        {
            e.PreventDefault();
            if (e.DataTransfer is { } transfer) transfer.DropEffect = "copy";
            _zone.ClassList.Add("is-over");
        });
        _zone.On<DragEvent>("dragleave", _ => _zone.ClassList.Remove("is-over"));
        _zone.On<DragEvent>("drop", e =>
        {
            e.PreventDefault();
            _zone.ClassList.Remove("is-over");
            if (e.DataTransfer is { } transfer) Add(transfer.Files);
        });

        var bar = stage.Add(Ui.El("div", "stage-controls"));
        bar.Add(Ui.Readout("Files", out _count, "0"));
        bar.Add(Ui.Readout("Total size", out _total, "0 B"));
        bar.Add(Ui.Button("Clear", Clear, "btn btn-ghost"));

        _list = stage.Add(Ui.El("ul", "files-list"));
    }

    public override void Unmount() => Clear();

    private void Add(FileList files)
    {
        for (var i = 0; i < files.Length; i++) Show(files[i]);
    }

    private void Show(File file)
    {
        _files++;
        _bytes += file.Size;
        _count.TextContent = _files.ToString();
        _total.TextContent = Controls.Bytes(_bytes);

        var row = Ui.El("li", "files-row");
        _list.Prepend(row);

        var preview = row.Add(Ui.El("div", "files-preview"));
        var kind = Kind(file);
        if (kind == "image")
        {
            // An object URL points straight at the file's bytes; it has to be revoked later, or
            // the file stays in memory for as long as the page does.
            var url = URL.CreateObjectURL(file);
            _objectUrls.Add(url);
            var image = preview.Add(Dom.Create<HTMLImageElement>("img"));
            image.Src = url;
            image.Alt = file.Name;
        }
        else
        {
            preview.Add(Ui.El("span", "files-ext", Extension(file.Name)));
        }

        var body = row.Add(Ui.El("div", "files-body"));
        var head = body.Add(Ui.El("div", "files-head"));
        head.Add(Ui.El("span", "files-name", file.Name));
        head.Add(Ui.El("span", "files-meta", $"{Controls.Bytes(file.Size)} · {(file.Type.Length > 0 ? file.Type : "unknown type")}"));

        var hash = body.Add(Ui.El("code", "files-hash", "hashing…"));
        Digest(file, hash);

        if (kind == "text") ReadText(file, body.Add(Ui.El("pre", "files-text")));
    }

    private static string Kind(File file)
    {
        if (file.Type.StartsWith("image/")) return "image";
        if (file.Type.StartsWith("text/") || file.Type is "application/json" or "application/xml" or "application/javascript")
            return "text";
        var extension = Extension(file.Name).ToLower();
        return extension is "cs" or "md" or "json" or "txt" or "csv" or "js" or "ts" or "xml" or "yml" or "yaml" ? "text" : "other";
    }

    private static string Extension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name.Substring(dot + 1).ToUpper() : "FILE";
    }

    /// <summary>SHA-256 of the whole file, written as lowercase hex — what <c>sha256sum</c> prints.</summary>
    private static async Task Digest(File file, HTMLElement target)
    {
        try
        {
            var started = Js.Performance.Now();
            var buffer = await file.ArrayBuffer();
            var digest = new Uint8Array(await Js.Crypto.Subtle.Digest("SHA-256", buffer));
            var hex = string.Concat(digest.ToArray().Select(b => b.ToString("x2")));
            target.TextContent = hex;
            target.Title = $"SHA-256, computed in {Js.Performance.Now() - started:F1} ms";
            target.ClassList.Add("is-done");
        }
        catch (Exception error)
        {
            target.TextContent = $"could not hash: {error.Message}";
            target.ClassList.Add("is-error");
        }
    }

    private static async Task ReadText(File file, HTMLElement target)
    {
        // Only the first few kilobytes: enough to recognise the file without reading a large one.
        var text = await file.Slice(0, 4096).Text();
        var lines = text.Split('\n').Take(8);
        target.TextContent = string.Join("\n", lines);
    }

    private void Clear()
    {
        foreach (var url in _objectUrls) URL.RevokeObjectURL(url);
        _objectUrls.Clear();
        _list.InnerHTML = "";
        _files = 0;
        _bytes = 0;
        _count.TextContent = "0";
        _total.TextContent = "0 B";
    }
}
