using System.Text.Json;
using CSJS;
using CSJS.Browser;

namespace CsjsApp;

/// <summary>One entry in the list.</summary>
public class TaskItem
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

/// <summary>
/// The page's behaviour, written in C#. It is transpiled to wwwroot/js/App.js and started from
/// index.html with <c>App.Start()</c>.
/// </summary>
[JsExport(Global = true)]
public static class App
{
    private const string StorageKey = "csjs-app.tasks";
    private static List<TaskItem> _tasks = new();

    public static void Start()
    {
        _tasks = Load();

        var form = Document.GetElementById("add-form");
        var input = Document.GetElementById("new-task");

        form?.AddEventListener("submit", e =>
        {
            e.PreventDefault();
            var title = input?.Value.Trim() ?? "";
            if (title.Length == 0) return;

            _tasks.Add(new TaskItem { Title = title });
            if (input is not null) input.Value = "";
            Save();
            Render();
        });

        Document.GetElementById("clear-done")?.AddEventListener("click", _ =>
        {
            var removed = _tasks.RemoveAll(t => t.Done);
            Console.WriteLine($"Removed {removed} finished task(s).");
            Save();
            Render();
        });

        Render();
    }

    private static void Render()
    {
        var list = Document.GetElementById("tasks");
        if (list is null) return;

        list.InnerHtml = "";
        for (var i = 0; i < _tasks.Count; i++)
        {
            var task = _tasks[i];
            var index = i;

            var item = Document.CreateElement("li");
            item.ClassList.Toggle("done", task.Done);

            var box = Document.CreateElement("input");
            box.SetAttribute("type", "checkbox");
            box.Checked = task.Done;
            box.AddEventListener("change", _ =>
            {
                _tasks[index].Done = box.Checked;
                Save();
                Render();
            });

            var label = Document.CreateElement("span");
            label.TextContent = task.Title;

            item.Append(box);
            item.Append(label);
            list.Append(item);
        }

        var remaining = _tasks.Count(t => !t.Done);
        var summary = Document.GetElementById("summary");
        if (summary is not null)
            summary.TextContent = remaining == 1 ? "1 task left" : $"{remaining} tasks left";
    }

    private static List<TaskItem> Load()
    {
        var json = Window.LocalStorage.GetItem(StorageKey);
        if (string.IsNullOrEmpty(json))
            return new List<TaskItem> { new() { Title = "Write C#" }, new() { Title = "Ship JavaScript", Done = true } };

        return JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new List<TaskItem>();
    }

    private static void Save() => Window.LocalStorage.SetItem(StorageKey, JsonSerializer.Serialize(_tasks));
}
