using CSJS.Web;

namespace CSJS.Gallery.Client.Shell;

/// <summary>A demo's page: what it shows, the live demo, and the seam beneath it.</summary>
public static class DemoPage
{
    public static void Render(HTMLElement main, Demo demo)
    {
        var page = main.Add(Ui.El("article", "demo-page"));

        var head = page.Add(Ui.El("header", "demo-head"));
        head.Add(Ui.El("p", "eyebrow", demo.Category));
        head.Add(Ui.El("h1", "demo-title", demo.Title));
        head.Add(Ui.El("p", "demo-summary", demo.Summary));

        var apis = head.Add(Ui.El("ul", "api-list"));
        foreach (var api in demo.Apis) apis.Add(Ui.El("li", "api-chip", api));

        var stage = page.Add(Ui.El("section", "stage"));
        stage.SetAttribute("aria-label", $"{demo.Title}, running");
        var stageBody = stage.Add(Ui.El("div", "stage-body"));

        try
        {
            demo.Mount(stageBody);
        }
        catch (Exception error)
        {
            stageBody.InnerHTML = $"<p class=\"stage-error\">This demo could not start: {Ui.Escape(error.Message)}</p>";
        }

        var seamSection = page.Add(Ui.El("section", "seam-section"));
        var seamHead = seamSection.Add(Ui.El("div", "section-head"));
        seamHead.Add(Ui.El("h2", "section-title", "The C#, and what it became"));
        seamHead.Add(Ui.El("p", "section-note", "Threads come from the module's own source map — the file your debugger reads."));

        var seam = new CodeSeam(seamSection.Add(Ui.El("div", "seam-view")));
        seam.Load(demo.Module);
    }
}
