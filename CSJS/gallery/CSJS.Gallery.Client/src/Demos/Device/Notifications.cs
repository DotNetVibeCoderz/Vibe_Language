using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Device;

/// <summary>
/// The page and the system around it: a notification permission flow, a notification sent
/// after a delay so you can switch away first, the vibration motor where there is one, and a
/// log of every time the page is hidden, shown, focused, or blurred.
/// </summary>
public sealed class Notifications : Demo
{
    public override string Id => "notifications";
    public override string Title => "Notifications and visibility";
    public override string Category => "Device";
    public override string Summary => "Ask for notification permission, send one after a delay, buzz the vibration motor on phones, and log the Page Visibility API's every change — switch tabs and come back to see it.";
    public override string[] Apis => new[] { "Notification", "Page Visibility", "navigator.vibrate", "focus and blur" };
    public override string Module => "Demos/Device/Notifications";
    public override string Hint => "Send one in five seconds, then switch tabs.";

    private HTMLElement _permission = null!;
    private HTMLElement _permissionNote = null!;
    private HTMLButtonElement _ask = null!;
    private HTMLButtonElement _send = null!;
    private HTMLInputElement _title = null!;
    private HTMLInputElement _body = null!;
    private HTMLElement _log = null!;
    private HTMLElement _visibility = null!;
    private HTMLElement _hiddenFor = null!;
    private double _timer = -1;
    private double _countdown = -1;
    private double _hiddenAt;
    private double _hiddenTotal;
    private readonly List<Notification> _open = new();
    private Action<Event> _visibilityChanged = null!;
    private Action<FocusEvent> _focus = null!;
    private Action<FocusEvent> _blur = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("stage-split");

        var left = stage.Add(Ui.El("div", "panel notify-panel"));

        left.Add(Ui.El("h3", "panel-title", "Notifications"));
        var permissionRow = left.Add(Ui.El("div", "notify-permission"));
        _permission = permissionRow.Add(Ui.El("span", "notify-badge", "…"));
        _permissionNote = permissionRow.Add(Ui.El("p", "notify-note"));
        _ask = permissionRow.Add(Ui.Button("Ask for permission", () => Ask(), "btn"));

        var form = left.Add(Ui.El("div", "notify-form"));
        _title = form.Add(Ui.Input("Title", "Built in C#"));
        _body = form.Add(Ui.Input("Message", "This notification was sent by C# that CSJS turned into JavaScript."));
        var actions = form.Add(Ui.El("div", "row"));
        _send = actions.Add(Ui.Button("Send in 5 seconds", () => Schedule(5), "btn btn-primary"));
        actions.Add(Ui.Button("Send now", () => Schedule(0)));

        left.Add(Ui.El("h3", "panel-title notify-gap", "Vibration"));
        var vibrate = left.Add(Ui.El("div", "row"));
        if (Controls.Supports(Js.Navigator, "vibrate"))
        {
            vibrate.Add(Ui.Button("Tap", () => Buzz("Tap", new[] { 40.0 })));
            vibrate.Add(Ui.Button("Heartbeat", () => Buzz("Heartbeat", new[] { 90.0, 120, 90 })));
            vibrate.Add(Ui.Button("SOS", () => Buzz("SOS", new[] { 80.0, 60, 80, 60, 80, 200, 240, 60, 240, 60, 240, 200, 80, 60, 80, 60, 80 })));
            vibrate.Add(Ui.El("span", "notify-note", "Works on phones with a motor; desktops accept the call and do nothing."));
        }
        else
        {
            vibrate.Add(Controls.Notice("No vibration here",
                "This browser has no navigator.vibrate. Chrome on Android has it; Safari and desktop browsers mostly do not."));
        }

        var right = stage.Add(Ui.El("div", "panel panel-log"));
        var head = right.Add(Ui.El("div", "notify-head"));
        head.Add(Ui.Readout("Page is", out _visibility, Js.Document.VisibilityState));
        head.Add(Ui.Readout("Hidden for", out _hiddenFor, "0 s"));
        right.Add(Ui.El("h3", "panel-title", "Visibility and focus"));
        _log = right.Add(Ui.Log());
        Ui.Write(_log, $"{Ui.Clock()}  opened — the page is {Js.Document.VisibilityState}", "ok");

        // These listeners are on document and window, which outlive the demo — Unmount takes
        // each one off again.
        _visibilityChanged = _ => OnVisibility();
        _focus = _ => Ui.Write(_log, $"{Ui.Clock()}  window focused", "in");
        _blur = _ => Ui.Write(_log, $"{Ui.Clock()}  window lost focus", "out");
        Js.Document.On("visibilitychange", _visibilityChanged);
        Js.Window.On("focus", _focus);
        Js.Window.On("blur", _blur);

        ShowPermission();
    }

    public override void Unmount()
    {
        Js.Document.Off("visibilitychange", _visibilityChanged);
        Js.Window.Off("focus", _focus);
        Js.Window.Off("blur", _blur);
        if (_timer >= 0) Js.ClearTimeout(_timer);
        if (_countdown >= 0) Js.ClearInterval(_countdown);
        foreach (var notification in _open) notification.Close();
    }

    private static bool Supported => Controls.Supports(Js.Window, "Notification");

    private void ShowPermission()
    {
        if (!Supported)
        {
            _permission.TextContent = "unsupported";
            _permission.ClassName = "notify-badge is-denied";
            _permissionNote.TextContent = "This browser has no Notification API. On iOS it exists only for web apps added to the home screen.";
            _ask.Disabled = true;
            _send.Disabled = true;
            return;
        }

        var state = Notification.Permission;
        _permission.TextContent = state;
        _permission.ClassName = $"notify-badge is-{state}";
        _ask.Disabled = state != "default";
        _permissionNote.TextContent = state switch
        {
            "granted" => "Notifications are allowed. They appear even while this tab is in the background.",
            "denied" => "Notifications were blocked. Allow them from the address bar's site settings, then reload.",
            _ => "The browser has not asked yet. It only will after a click — never on page load.",
        };
    }

    private async Task Ask()
    {
        var result = await Notification.RequestPermission();
        Ui.Write(_log, $"{Ui.Clock()}  permission → {result}", result == "granted" ? "ok" : "warn");
        ShowPermission();
    }

    private void Schedule(int seconds)
    {
        if (!Supported) return;
        if (Notification.Permission != "granted")
        {
            Ui.Write(_log, $"{Ui.Clock()}  not sent — permission is {Notification.Permission}", "warn");
            if (Notification.Permission == "default") Ask();
            return;
        }

        if (_timer >= 0) Js.ClearTimeout(_timer);
        if (_countdown >= 0) Js.ClearInterval(_countdown);

        if (seconds == 0)
        {
            Send();
            return;
        }

        var remaining = seconds;
        _send.TextContent = $"Sending in {remaining}…";
        _countdown = Js.SetInterval(() =>
        {
            remaining--;
            _send.TextContent = remaining > 0 ? $"Sending in {remaining}…" : "Send in 5 seconds";
        }, 1000);
        _timer = Js.SetTimeout(() =>
        {
            Js.ClearInterval(_countdown);
            _countdown = -1;
            _timer = -1;
            _send.TextContent = "Send in 5 seconds";
            Send();
        }, seconds * 1000);
        Ui.Write(_log, $"{Ui.Clock()}  notification scheduled for {seconds} s from now", "");
    }

    private void Send()
    {
        var notification = new Notification(_title.Value.Length > 0 ? _title.Value : "CSJS Gallery", new NotificationOptions
        {
            Body = _body.Value,
            Icon = "img/icon.png",
            Tag = "csjs-gallery",
        });
        _open.Add(notification);
        notification.On<Event>("click", _ =>
        {
            Js.Window.Focus();
            notification.Close();
            Ui.Write(_log, $"{Ui.Clock()}  notification clicked — back to the page", "ok");
        });
        notification.On<Event>("close", _ => _open.Remove(notification));
        Ui.Write(_log, $"{Ui.Clock()}  notification shown while the page was {Js.Document.VisibilityState}", "ok");
    }

    private void Buzz(string name, double[] pattern)
    {
        var accepted = Js.Navigator.Vibrate(pattern);
        Ui.Write(_log, $"{Ui.Clock()}  vibrate “{name}” — {(accepted ? "accepted" : "refused (it needs a tap on the page first)")}", accepted ? "" : "warn");
    }

    private void OnVisibility()
    {
        var state = Js.Document.VisibilityState;
        _visibility.TextContent = state;
        if (state == "hidden")
        {
            _hiddenAt = Js.Performance.Now();
            Ui.Write(_log, $"{Ui.Clock()}  hidden — another tab, a minimised window, or a locked screen", "out");
            return;
        }

        var away = (Js.Performance.Now() - _hiddenAt) / 1000;
        _hiddenTotal += away;
        _hiddenFor.TextContent = $"{_hiddenTotal:F1} s";
        Ui.Write(_log, $"{Ui.Clock()}  visible again after {away:F1} s away", "in");
    }
}
