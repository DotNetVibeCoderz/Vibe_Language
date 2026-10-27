using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Media;

/// <summary>
/// The camera through <c>getUserMedia</c>: a live preview with CSS filters, and snapshots drawn
/// through the same filter onto a canvas so what you keep is what you saw. Every failure — no
/// camera, permission refused, an insecure page — gets its own explanation.
/// </summary>
public sealed class Camera : Demo
{
    public override string Id => "camera";
    public override string Title => "Camera";
    public override string Category => "Media";
    public override string Summary => "getUserMedia into a video element, filters applied live with CSS, and snapshots taken through the same filter on a canvas. The stream's tracks are stopped the moment you leave.";
    public override string[] Apis => new[] { "MediaDevices.getUserMedia", "MediaStream", "HTMLVideoElement", "Canvas 2D filter" };
    public override string Module => "Demos/Media/Camera";
    public override string Hint => "Turn the camera on, pick a look, take a snapshot.";

    private static readonly string[] Looks = { "None", "Mono", "Sepia", "Vivid", "Cool", "Noir", "Invert" };

    private HTMLElement _viewfinder = null!;
    private HTMLVideoElement _video = null!;
    private HTMLElement _overlay = null!;
    private HTMLElement _strip = null!;
    private HTMLButtonElement _toggle = null!;
    private HTMLButtonElement _snap = null!;
    private HTMLElement _resolution = null!;
    private HTMLElement _device = null!;
    private MediaStream? _stream;
    private string _look = "None";
    private int _shots;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("camera-stage");

        _viewfinder = stage.Add(Ui.El("div", "camera-viewfinder"));
        _video = _viewfinder.Add(Dom.Create<HTMLVideoElement>("video"));
        _video.Muted = true;
        _video.PlaysInline = true;
        _video.SetAttribute("aria-label", "Camera preview");
        _overlay = _viewfinder.Add(Ui.El("div", "camera-overlay"));
        ShowOff();

        var controls = stage.Add(Ui.El("div", "stage-controls"));
        _toggle = controls.Add(Ui.Button("Turn camera on", () => Toggle(), "btn btn-primary"));
        _snap = controls.Add(Ui.Button("Take snapshot", Snap));
        _snap.Disabled = true;
        controls.Add(Controls.Segmented("Look", Looks, _look, look =>
        {
            _look = look;
            _video.Style.Filter = Filter(look);
        }));
        controls.Add(Ui.Readout("Resolution", out _resolution));
        controls.Add(Ui.Readout("Device", out _device));

        var shelf = stage.Add(Ui.El("div", "camera-shelf"));
        shelf.Add(Ui.El("h3", "panel-title", "Snapshots"));
        _strip = shelf.Add(Ui.El("div", "camera-strip"));
        _strip.Add(Ui.El("p", "empty", "Snapshots land here. Each one is a PNG you can save."));
    }

    public override void Unmount() => Stop();

    /// <summary>The CSS filter for a look — the same string the canvas uses for snapshots.</summary>
    private static string Filter(string look) => look switch
    {
        "Mono" => "grayscale(1) contrast(1.1)",
        "Sepia" => "sepia(0.85) saturate(1.2)",
        "Vivid" => "saturate(1.8) contrast(1.15)",
        "Cool" => "hue-rotate(190deg) saturate(1.3)",
        "Noir" => "grayscale(1) contrast(1.8) brightness(0.85)",
        "Invert" => "invert(1)",
        _ => "none",
    };

    private async Task Toggle()
    {
        if (_stream is not null)
        {
            Stop();
            return;
        }

        // navigator.mediaDevices only exists in a secure context: HTTPS, or localhost.
        if (!Controls.Supports(Js.Navigator, "mediaDevices"))
        {
            ShowProblem("Camera access needs a secure page",
                "Browsers only offer the camera to pages served over HTTPS or from localhost. Open the gallery that way and try again.");
            return;
        }

        _toggle.Disabled = true;
        _toggle.TextContent = "Waiting for permission…";
        try
        {
            _stream = await Js.Navigator.MediaDevices.GetUserMedia(new MediaStreamConstraints
            {
                Video = new MediaTrackConstraints { Width = 1280, Height = 720, FacingMode = "user" },
                Audio = false,
            });

            _video.SrcObject = _stream;
            await _video.Play();
            _overlay.InnerHTML = "";
            _overlay.ClassList.Add("is-live");
            _viewfinder.ClassList.Add("is-live");
            _snap.Disabled = false;
            _toggle.TextContent = "Turn camera off";

            var track = _stream.GetVideoTracks()[0];
            _resolution.TextContent = $"{_video.VideoWidth:F0} × {_video.VideoHeight:F0}";
            _device.TextContent = track.Label.Length > 0 ? Shorten(track.Label) : "Camera";
        }
        catch (Exception error)
        {
            _stream = null;
            _toggle.TextContent = "Turn camera on";
            // getUserMedia rejects with a DOMException whose name says what went wrong.
            switch (error.As<DOMException>().Name)
            {
                case "NotAllowedError":
                    ShowProblem("Camera permission was refused",
                        "Allow the camera for this site — usually from the icon at the left of the address bar — then turn it on again.");
                    break;
                case "NotFoundError":
                case "OverconstrainedError":
                    ShowProblem("No camera was found",
                        "This device has no camera the browser can use. Connect one, or try the gallery on a phone or laptop.");
                    break;
                case "NotReadableError":
                    ShowProblem("The camera is busy",
                        "Another application is using it. Close that application, then turn the camera on again.");
                    break;
                default:
                    ShowProblem("The camera could not start", error.Message);
                    break;
            }
        }
        finally
        {
            _toggle.Disabled = false;
        }
    }

    /// <summary>Stops every track. Detaching the video alone would leave the camera light on.</summary>
    private void Stop()
    {
        if (_stream is not null)
            foreach (var track in _stream.GetTracks()) track.Stop();
        _stream = null;
        _video.SrcObject = null;
        _snap.Disabled = true;
        _toggle.TextContent = "Turn camera on";
        _resolution.TextContent = "—";
        _device.TextContent = "—";
        ShowOff();
    }

    private void ShowOff()
    {
        _viewfinder.ClassList.Remove("is-live");
        _overlay.ClassList.Remove("is-live");
        _overlay.InnerHTML = "";
        _overlay.Add(Ui.El("span", "camera-corners"));
        _overlay.Add(Controls.Notice("The camera is off",
            "Turn it on to see a live preview. The browser will ask first, and nothing leaves this page — frames are only drawn here."));
    }

    private void ShowProblem(string title, string body)
    {
        _overlay.InnerHTML = "";
        _overlay.Add(Ui.El("span", "camera-corners"));
        _overlay.Add(Controls.Notice(title, body, "warn"));
    }

    /// <summary>Draws the current frame, through the current look, onto a canvas and keeps it.</summary>
    private void Snap()
    {
        if (_stream is null || _video.VideoWidth == 0) return;

        var canvas = Dom.Create<HTMLCanvasElement>("canvas");
        canvas.Width = _video.VideoWidth;
        canvas.Height = _video.VideoHeight;
        var ctx = canvas.GetContext2D();
        ctx.Filter = Filter(_look);
        // The preview is mirrored like a mirror; the snapshot is mirrored to match it.
        ctx.Translate(canvas.Width, 0);
        ctx.Scale(-1, 1);
        ctx.DrawImage(_video, 0, 0, canvas.Width, canvas.Height);

        if (_shots == 0) _strip.InnerHTML = "";
        _shots++;

        var link = Dom.Create<HTMLAnchorElement>("a");
        link.ClassName = "camera-shot";
        link.Href = canvas.ToDataURL("image/png");
        link.Download = $"csjs-snapshot-{_shots}.png";
        link.Title = "Save this snapshot";
        var image = link.Add(Dom.Create<HTMLImageElement>("img"));
        image.Src = link.Href;
        image.Alt = $"Snapshot {_shots}, {_look.ToLower()} look";
        link.Add(Ui.El("span", "camera-shot-label", $"#{_shots} · {_look}"));
        _strip.Prepend(link);
    }

    private static string Shorten(string label) => label.Length > 28 ? label.Substring(0, 27) + "…" : label;
}
