using CSJS.Gallery.Client.Shell;
using CSJS.Web;
using JsObject = CSJS.Browser.JsObject;

namespace CSJS.Gallery.Client.Demos.Data;

/// <summary>
/// The async Clipboard API in both directions: text written and read back, an image drawn on a
/// canvas and copied as a PNG through <c>ClipboardItem</c>, and the page's own <c>paste</c> event
/// catching whatever you paste into it.
/// </summary>
public sealed class ClipboardDemo : Demo
{
    public override string Id => "clipboard";
    public override string Title => "Clipboard";
    public override string Category => "Data";
    public override string Summary => "Copy and read text with navigator.clipboard, copy a canvas as a PNG through ClipboardItem, and catch pasted text or images with the paste event — each permission outcome explained.";
    public override string[] Apis => new[] { "Clipboard.writeText", "Clipboard.readText", "ClipboardItem", "paste event" };
    public override string Module => "Demos/Data/ClipboardDemo";
    public override string Hint => "Copy the card image, then paste it into a chat or a document.";

    private HTMLTextAreaElement _text = null!;
    private HTMLElement _read = null!;
    private HTMLCanvasElement _card = null!;
    private HTMLElement _pasted = null!;
    private HTMLElement _log = null!;
    private Action<ClipboardEvent> _paste = null!;
    private readonly List<string> _objectUrls = new();

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        var left = stage.Add(Ui.El("div", "panel clip-panel"));

        left.Add(Ui.El("h3", "panel-title", "Text"));
        _text = left.Add(Dom.Create<HTMLTextAreaElement>("textarea"));
        _text.ClassName = "field clip-text";
        _text.Rows = 2;
        _text.Value = "Copied by C#, through navigator.clipboard.writeText.";
        _text.SetAttribute("aria-label", "Text to copy");
        var textActions = left.Add(Ui.El("div", "row"));
        textActions.Add(Ui.Button("Copy text", () => CopyText(), "btn btn-primary"));
        textActions.Add(Ui.Button("Read clipboard", () => ReadText()));
        _read = left.Add(Ui.El("p", "clip-read", "Read clipboard shows what is on it now. The browser asks first."));

        left.Add(Ui.El("h3", "panel-title clip-gap", "Image"));
        var imageRow = left.Add(Ui.El("div", "clip-image-row"));
        _card = imageRow.Add(Dom.Create<HTMLCanvasElement>("canvas"));
        _card.ClassName = "clip-card";
        _card.Width = 640;
        _card.Height = 336;
        DrawCard();
        var imageActions = imageRow.Add(Ui.El("div", "clip-image-actions"));
        imageActions.Add(Ui.Button("Copy as PNG", () => CopyImage(), "btn btn-primary"));
        imageActions.Add(Ui.Button("Redraw", DrawCard));

        var right = stage.Add(Ui.El("div", "panel panel-log clip-panel"));
        right.Add(Ui.El("h3", "panel-title", "Paste anywhere on this page"));
        _pasted = right.Add(Ui.El("div", "clip-pasted"));
        _pasted.Add(Ui.El("p", "empty", "Press Ctrl+V (⌘V on a Mac). Text and images both land here."));
        right.Add(Ui.El("h3", "panel-title clip-gap", "Log"));
        _log = right.Add(Ui.Log());

        // The paste event fires on whatever has focus, so the listener sits on document. It is
        // synchronous and needs no permission: the user's own keystroke is the consent.
        _paste = e => OnPaste(e);
        Js.Document.On("paste", _paste);

        if (!Controls.Supports(Js.Navigator, "clipboard"))
            Ui.Write(_log, "navigator.clipboard is missing — it needs a secure page (HTTPS or localhost).", "warn");
    }

    public override void Unmount()
    {
        Js.Document.Off("paste", _paste);
        foreach (var url in _objectUrls) URL.RevokeObjectURL(url);
    }

    private async Task CopyText()
    {
        try
        {
            await Js.Navigator.Clipboard.WriteText(_text.Value);
            Ui.Write(_log, $"{Ui.Clock()}  copied {_text.Value.Length} characters", "ok");
        }
        catch (Exception error)
        {
            Explain("copy text", error);
        }
    }

    private async Task ReadText()
    {
        try
        {
            var text = await Js.Navigator.Clipboard.ReadText();
            _read.TextContent = text.Length > 0 ? text : "The clipboard holds no text.";
            _read.ClassList.Add("is-filled");
            Ui.Write(_log, $"{Ui.Clock()}  read {text.Length} characters", "ok");
        }
        catch (Exception error)
        {
            Explain("read the clipboard", error);
        }
    }

    /// <summary>
    /// Copies the canvas as a PNG. <c>ClipboardItem</c> maps MIME types to blobs — here, one entry.
    /// The blob is passed as a pending Task: Safari only accepts the write if it starts within the
    /// click, before the image has finished encoding.
    /// </summary>
    private async Task CopyImage()
    {
        if (!Controls.Supports(Js.Window, "ClipboardItem"))
        {
            Ui.Write(_log, "This browser has no ClipboardItem, so it can copy text but not images.", "warn");
            return;
        }

        try
        {
            var items = new JsObject();
            items.Set("image/png", Png(_card));
            await Js.Navigator.Clipboard.Write(new[] { new ClipboardItem(items) });
            Ui.Write(_log, $"{Ui.Clock()}  copied a {_card.Width:F0}×{_card.Height:F0} PNG — paste it into another app", "ok");
        }
        catch (Exception error)
        {
            Explain("copy the image", error);
        }
    }

    // canvas.toBlob takes a callback; a Promise around it makes it awaitable and lets the
    // ClipboardItem receive it while it is still pending.
    [CSJS.JsTemplate("new Promise((resolve) => {0}.toBlob(resolve, 'image/png'))")]
    private static Task<Blob> Png(HTMLCanvasElement canvas) => throw new CSJS.JsExternException();

    private void Explain(string action, Exception error)
    {
        var name = error.As<DOMException>().Name;
        var message = name switch
        {
            "NotAllowedError" => $"Could not {action}: permission was refused, or the page was not focused. Click the page and try again.",
            "SecurityError" => $"Could not {action}: the clipboard needs a secure page (HTTPS or localhost).",
            "DataError" => $"Could not {action}: the browser does not accept that format.",
            _ => $"Could not {action}: {error.Message}",
        };
        Ui.Write(_log, $"{Ui.Clock()}  {message}", "error");
    }

    private void OnPaste(ClipboardEvent e)
    {
        if (e.ClipboardData is not { } data) return;
        // Pasting into the demo's own text box should still just paste.
        if (e.Target is HTMLTextAreaElement or HTMLInputElement) return;

        var files = data.Files;
        if (files.Length > 0 && files[0].Type.StartsWith("image/"))
        {
            var url = URL.CreateObjectURL(files[0]);
            _objectUrls.Add(url);
            _pasted.InnerHTML = "";
            var image = _pasted.Add(Dom.Create<HTMLImageElement>("img"));
            image.Src = url;
            image.Alt = "Pasted image";
            Ui.Write(_log, $"{Ui.Clock()}  pasted an image · {files[0].Type} · {Controls.Bytes(files[0].Size)}", "in");
            e.PreventDefault();
            return;
        }

        var text = data.GetData("text/plain");
        if (text.Length == 0) return;
        _pasted.InnerHTML = "";
        _pasted.Add(Ui.El("p", "clip-pasted-text", text));
        Ui.Write(_log, $"{Ui.Clock()}  pasted {text.Length} characters of text", "in");
        e.PreventDefault();
    }

    /// <summary>A small card worth pasting: the seam, a title, and the moment it was drawn.</summary>
    private void DrawCard()
    {
        var ctx = _card.GetContext2D();
        var w = _card.Width;
        var h = _card.Height;

        ctx.FillStyle = "#17142a";
        ctx.FillRect(0, 0, w, h);

        var glow = ctx.CreateRadialGradient(w * 0.15, 0, 10, w * 0.15, 0, w * 0.7);
        glow.AddColorStop(0, "rgba(167, 139, 250, 0.35)");
        glow.AddColorStop(1, "rgba(167, 139, 250, 0)");
        ctx.FillStyle = glow;
        ctx.FillRect(0, 0, w, h);

        var seam = ctx.CreateLinearGradient(0, 0, w, 0);
        seam.AddColorStop(0, "#a78bfa");
        seam.AddColorStop(0.5, "#f0abfc");
        seam.AddColorStop(1, "#fbbf24");
        ctx.FillStyle = seam;
        ctx.FillRect(0, h - 10, w, 10);

        ctx.FillStyle = "#a78bfa";
        ctx.Font = "500 20px 'Martian Mono', monospace";
        ctx.FillText("CSJS GALLERY", 44, 70);

        ctx.FillStyle = "#eeeaf8";
        ctx.Font = "700 58px 'Bricolage Grotesque', sans-serif";
        ctx.FillText("Copied from C#", 44, 160);

        ctx.FillStyle = "#a39dbd";
        ctx.Font = "22px 'JetBrains Mono', monospace";
        ctx.FillText($"drawn {DateTime.Now:HH:mm:ss}  ·  {Random.Shared.Next(1000, 9999)}", 44, 214);

        ctx.FillStyle = "#fbbf24";
        ctx.Font = "500 18px 'Martian Mono', monospace";
        ctx.FillText("canvas → PNG → ClipboardItem", 44, 280);
    }
}
