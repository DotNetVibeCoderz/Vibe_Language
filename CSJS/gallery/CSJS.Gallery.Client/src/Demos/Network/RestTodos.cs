using System.Text.Json;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Network;

/// <summary>The shape <c>/api/todos</c> returns — the same record the server serialises.</summary>
public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public DateTime Created { get; set; }
}

/// <summary>
/// A todo list against a real REST API: GET, POST, PUT, DELETE through <c>fetch</c>, with bodies
/// written and read by <c>System.Text.Json</c> — the same calls the code would make on .NET. Every
/// request lands in the log with its status and how long it took.
/// </summary>
public sealed class RestTodos : Demo
{
    public override string Id => "rest-todos";
    public override string Title => "REST";
    public override string Category => "Network";
    public override string Summary => "A todo list against a live ASP.NET Core API. Requests go through fetch; bodies are written and read with System.Text.Json, exactly as on .NET.";
    public override string[] Apis => new[] { "fetch", "Headers", "System.Text.Json", "performance.now" };
    public override string Module => "Demos/Network/RestTodos";
    public override string Hint => "Add, tick, and delete — then read the request log.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private HTMLElement _list = null!;
    private HTMLElement _log = null!;
    private HTMLInputElement _input = null!;
    private List<TodoItem> _items = new();

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        var app = stage.Add(Ui.El("div", "panel"));
        var form = app.Add(Dom.Create<HTMLFormElement>("form"));
        form.ClassName = "row";
        _input = form.Add(Ui.Input("What needs doing?"));
        form.Add(Ui.Button("Add", () => { }, "btn btn-primary")).Type = "submit";
        form.On<SubmitEvent>("submit", async e =>
        {
            e.PreventDefault();
            await Add(_input.Value);
        });
        _list = app.Add(Ui.El("ul", "todo-list"));

        var side = stage.Add(Ui.El("div", "panel panel-log"));
        side.Add(Ui.El("h3", "panel-title", "Requests"));
        _log = side.Add(Ui.Log());

        Load();
    }

    private async Task Load()
    {
        var response = await Send("GET", "/api/todos", null);
        if (response is null) return;
        _items = JsonSerializer.Deserialize<List<TodoItem>>(await response.Text(), Json) ?? new();
        Render();
    }

    private async Task Add(string title)
    {
        var response = await Send("POST", "/api/todos", JsonSerializer.Serialize(new { title }));
        if (response is null) return;

        if (response.Status == 400)
        {
            Ui.Write(_log, "The server wants a title — nothing was added.", "warn");
            return;
        }

        var created = JsonSerializer.Deserialize<TodoItem>(await response.Text(), Json)!;
        _items.Add(created);
        _input.Value = "";
        Render();
    }

    private async Task Toggle(TodoItem item)
    {
        var response = await Send("PUT", $"/api/todos/{item.Id}/toggle", null);
        if (response is null || !response.Ok) return;
        var updated = JsonSerializer.Deserialize<TodoItem>(await response.Text(), Json)!;
        item.Done = updated.Done;
        Render();
    }

    private async Task Remove(TodoItem item)
    {
        var response = await Send("DELETE", $"/api/todos/{item.Id}", null);
        if (response is null || !response.Ok) return;
        _items.Remove(item);
        Render();
    }

    /// <summary>Sends a request and logs it. Returns null when the network itself failed.</summary>
    private async Task<Response?> Send(string method, string url, string? body)
    {
        var headers = new Headers();
        headers.Set("Accept", "application/json");
        if (body is not null) headers.Set("Content-Type", "application/json");

        var started = Js.Performance.Now();
        try
        {
            var response = await Js.Fetch(url, new RequestInit { Method = method, Headers = headers, Body = body });
            var elapsed = Js.Performance.Now() - started;
            Ui.Write(_log, $"{method} {url}  →  {response.Status} {response.StatusText}  ·  {elapsed:F0} ms", response.Ok ? "ok" : "warn");
            return response;
        }
        catch (Exception error)
        {
            Ui.Write(_log, $"{method} {url}  →  failed: {error.Message}", "error");
            return null;
        }
    }

    private void Render()
    {
        _list.InnerHTML = "";
        if (_items.Count == 0)
        {
            _list.Add(Ui.El("li", "empty", "Nothing to do. Add something above."));
            return;
        }

        foreach (var item in _items.OrderBy(i => i.Done).ThenBy(i => i.Id))
        {
            var row = _list.Add(Ui.El("li", item.Done ? "todo is-done" : "todo"));
            var check = row.Add(Dom.Create<HTMLInputElement>("input"));
            check.Type = "checkbox";
            check.Checked = item.Done;
            check.On<Event>("change", _ => Toggle(item));
            row.Add(Ui.El("span", "todo-title", item.Title));
            row.Add(Ui.El("time", "todo-time", item.Created.ToString("HH:mm")));
            row.Add(Ui.Button("Delete", () => Remove(item), "btn btn-ghost btn-small"));
        }
    }
}
