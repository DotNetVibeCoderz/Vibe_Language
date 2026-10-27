using CSJS;
using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>
/// The gallery's entry point: builds the rail, then routes between the home page and a demo page
/// on <c>location.hash</c> — <c>#/</c> and <c>#/demo/{id}</c> — so every demo has a link of its
/// own and the back button works.
/// </summary>
[JsExport(Global = true)]
public static class GalleryApp
{
    private static Demo? _current;
    private static HTMLElement _main = null!;

    public static void Start()
    {
        _main = Dom.ById<HTMLElement>("main")!;
        BuildRail();
        Js.Window.On<Event>("hashchange", _ => Route());
        Route();
    }

    private static void BuildRail()
    {
        var rail = Dom.ById<HTMLElement>("rail-index")!;
        rail.InnerHTML = "";

        foreach (var category in Catalog.Categories)
        {
            var demos = Catalog.In(category.Name);
            if (demos.Count == 0) continue;

            var group = rail.Add(Ui.El("section", "rail-group"));
            var heading = group.Add(Ui.El("h2", "rail-heading"));
            heading.Add(Ui.El("span", "", category.Name));
            heading.Add(Ui.El("span", "rail-count", demos.Count.ToString()));

            var list = group.Add(Ui.El("ul", "rail-list"));
            foreach (var demo in demos)
            {
                var link = list.Add(Ui.El("li")).Add(Dom.Create<HTMLAnchorElement>("a"));
                link.Href = $"#/demo/{demo.Id}";
                link.ClassName = "rail-link";
                link.SetAttribute("data-demo", demo.Id);
                link.TextContent = demo.Title;
            }
        }
    }

    private static void Route()
    {
        _current?.Unmount();
        _current = null;

        var hash = Js.Location.Hash;
        var id = hash.StartsWith("#/demo/") ? hash.Substring(7) : "";
        var demo = id.Length > 0 ? Catalog.Find(id) : null;

        foreach (var link in Dom.QueryAll<HTMLAnchorElement>(".rail-link"))
            link.ClassList.Toggle("is-active", link.GetAttribute("data-demo") == id);

        _main.InnerHTML = "";
        _main.ScrollTo(0, 0);

        if (demo is null)
        {
            Js.Document.Title = "CSJS Gallery — C# that runs as JavaScript";
            HomePage.Render(_main);
            return;
        }

        Js.Document.Title = $"{demo.Title} · CSJS Gallery";
        _current = demo;
        DemoPage.Render(_main, demo);
    }
}
