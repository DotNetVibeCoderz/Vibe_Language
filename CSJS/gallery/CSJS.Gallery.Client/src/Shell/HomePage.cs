using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>
/// The front page. Its hero is the seam itself, showing the C# of the source-map decoder that is
/// drawing those very threads — the clearest way to say what CSJS is in one screen.
/// </summary>
public static class HomePage
{
    public static void Render(HTMLElement main)
    {
        var page = main.Add(Ui.El("div", "home"));

        var hero = page.Add(Ui.El("section", "hero"));
        var copy = hero.Add(Ui.El("div", "hero-copy"));
        copy.Add(Ui.El("p", "eyebrow", $"CSJS Gallery  ·  {Catalog.All.Count} demos  ·  {Catalog.Categories.Count(c => Catalog.In(c.Name).Count > 0)} categories"));
        var title = copy.Add(Ui.El("h1", "hero-title"));
        title.InnerHTML = "Written in <em class=\"cs\">C#</em>.<br/>Running as <em class=\"js\">JavaScript</em>.";
        copy.Add(Ui.El("p", "hero-lede",
            "Every demo here is C# that CSJS transpiled into the module your browser is running right now — " +
            "canvas, audio, the camera, workers, and six ways to talk to a server. Each one sits beside the " +
            "JavaScript it became, joined at the seam."));

        var actions = copy.Add(Ui.El("div", "hero-actions"));
        var first = Catalog.All.FirstOrDefault();
        if (first is not null)
        {
            var open = actions.Add(Dom.Create<HTMLAnchorElement>("a"));
            open.Href = $"#/demo/{first.Id}";
            open.ClassName = "btn btn-primary";
            open.TextContent = "Open the first demo";
        }
        var api = actions.Add(Dom.Create<HTMLAnchorElement>("a"));
        api.Href = "#/demo/rest-todos";
        api.ClassName = "btn";
        api.TextContent = "Try the API playground";

        var heroSeam = hero.Add(Ui.El("figure", "hero-seam"));
        var caption = heroSeam.Add(Ui.El("figcaption", "hero-caption"));
        caption.InnerHTML = "The threads are drawn by the code on the left: <code>SourceMap.cs</code>, decoding its own map.";
        var seam = new CodeSeam(heroSeam.Add(Ui.El("div", "seam-view")));
        seam.Load("Shell/SourceMap", "private static List<int> DecodeSegment");

        foreach (var category in Catalog.Categories)
        {
            var demos = Catalog.In(category.Name);
            if (demos.Count == 0) continue;

            var section = page.Add(Ui.El("section", "shelf"));
            var head = section.Add(Ui.El("header", "shelf-head"));
            head.Add(Ui.El("h2", "shelf-title", category.Name));
            head.Add(Ui.El("p", "shelf-blurb", category.Blurb));

            var grid = section.Add(Ui.El("div", "card-grid"));
            foreach (var demo in demos) grid.Add(Card(demo));
        }

        page.Add(StartYourOwn());
    }

    private static HTMLElement Card(Demo demo)
    {
        var card = Dom.Create<HTMLAnchorElement>("a");
        card.Href = $"#/demo/{demo.Id}";
        card.ClassName = "card";
        card.Add(Ui.El("h3", "card-title", demo.Title));
        card.Add(Ui.El("p", "card-summary", demo.Summary));
        if (demo.Hint.Length > 0) card.Add(Ui.El("p", "card-hint", demo.Hint));

        var apis = card.Add(Ui.El("ul", "card-apis"));
        foreach (var api in demo.Apis.Take(3)) apis.Add(Ui.El("li", "", api));
        if (demo.Apis.Length > 3) apis.Add(Ui.El("li", "", $"+{demo.Apis.Length - 3}"));
        return card;
    }

    private static HTMLElement StartYourOwn()
    {
        var section = Ui.El("section", "start");
        section.Add(Ui.El("h2", "start-title", "Start your own"));
        section.Add(Ui.El("p", "start-lede", "An ordinary .csproj — full IntelliSense in Visual Studio, Rider, and VS Code, and CSJS errors in the error list."));

        const string command = "dotnet new install CSJS.Templates\ndotnet new csjs-app -n MyApp\ncd MyApp && dotnet run";
        var block = section.Add(Ui.El("div", "command"));
        block.Add(Ui.El("pre", "command-text", command));

        var copy = Ui.Button("Copy", () => { }, "btn btn-small");
        copy.On<MouseEvent>("click", async _ =>
        {
            await Js.Navigator.Clipboard.WriteText(command);
            copy.TextContent = "Copied";
            Js.SetTimeout(() => copy.TextContent = "Copy", 1600);
        });
        block.Add(copy);
        return section;
    }
}
