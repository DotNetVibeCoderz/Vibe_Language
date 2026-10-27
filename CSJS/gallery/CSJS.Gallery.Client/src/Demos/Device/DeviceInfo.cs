using CSJS;
using CSJS.Gallery.Client.Shell;
using CSJS.Web;

namespace CSJS.Gallery.Client.Demos.Device;

// The Battery Status API and navigator.connection are Chromium-only and absent from lib.dom, so
// the members used here are declared as bindings.

/// <summary>A battery's state, from <c>navigator.getBattery()</c>.</summary>
[JsGlobal("BatteryManager")]
public class BatteryManager : EventTarget
{
    public bool Charging => throw new JsExternException();
    public double Level => throw new JsExternException();
    public double ChargingTime => throw new JsExternException();
    public double DischargingTime => throw new JsExternException();
}

/// <summary>The network's estimated quality, from <c>navigator.connection</c>.</summary>
[JsGlobal("NetworkInformation")]
public class NetworkInformation : EventTarget
{
    public string EffectiveType => throw new JsExternException();
    public double Downlink => throw new JsExternException();
    public double Rtt => throw new JsExternException();
    public bool SaveData => throw new JsExternException();
}

public static class NavigatorExtras
{
    [JsTemplate("navigator.getBattery()")]
    public static Task<BatteryManager> GetBattery() => throw new JsExternException();

    [JsTemplate("navigator.connection")]
    public static NetworkInformation Connection => throw new JsExternException();

    [JsTemplate("(navigator.deviceMemory ?? 0)")]
    public static double DeviceMemory => throw new JsExternException();
}

/// <summary>
/// What the browser will say about the machine it is running on — and which of those answers
/// change while you watch: the connection, the window, the colour scheme, the battery.
/// </summary>
public sealed class DeviceInfo : Demo
{
    public override string Id => "device-info";
    public override string Title => "Device and screen";
    public override string Category => "Device";
    public override string Summary => "navigator, screen, and media queries read from C#, with every live value subscribed to: go offline, resize the window, or switch your system to light mode and watch the cards change.";
    public override string[] Apis => new[] { "Navigator", "Screen", "matchMedia", "online and offline", "Battery Status" };
    public override string Module => "Demos/Device/DeviceInfo";
    public override string Hint => "Resize the window or toggle your system's dark mode.";

    /// <summary>A media query this demo watches, and the element showing its answer.</summary>
    private sealed class Watched
    {
        public MediaQueryList Query = null!;
        public HTMLElement Value = null!;
        public Action<MediaQueryListEvent> Listener = null!;
    }

    private readonly List<Watched> _queries = new();
    private HTMLElement _online = null!;
    private HTMLElement _viewport = null!;
    private HTMLElement _ratio = null!;
    private HTMLElement _orientation = null!;
    private HTMLElement _log = null!;
    private Action<Event> _connectivity = null!;
    private Action<UIEvent> _resized = null!;
    private BatteryManager? _battery;
    private Action<Event> _batteryChanged = null!;
    private HTMLElement _batteryCard = null!;

    public override void Mount(HTMLElement stage)
    {
        stage.ClassList.Add("info-stage");
        var grid = stage.Add(Ui.El("div", "info-grid"));

        // ---- The browser -------------------------------------------------------------------
        var browser = Card(grid, "Browser");
        var facts = browser.Add(Ui.El("dl", "facts"));
        Controls.Fact(facts, "Language", Js.Navigator.Language);
        Controls.Fact(facts, "Preferred", string.Join(", ", Js.Navigator.Languages.Take(4)));
        Controls.Fact(facts, "Time zone", TimeZone());
        Controls.Fact(facts, "Logical cores", Js.Navigator.HardwareConcurrency.ToString());
        var memory = NavigatorExtras.DeviceMemory;
        Controls.Fact(facts, "Device memory", memory > 0 ? $"≥ {memory} GB" : "not reported");
        Controls.Fact(facts, "Touch points", Js.Navigator.MaxTouchPoints.ToString());
        Controls.Fact(facts, "Cookies", Js.Navigator.CookieEnabled ? "enabled" : "disabled");

        // ---- The screen --------------------------------------------------------------------
        var screen = Card(grid, "Screen");
        var screenFacts = screen.Add(Ui.El("dl", "facts"));
        Controls.Fact(screenFacts, "Screen", $"{Js.Screen.Width:F0} × {Js.Screen.Height:F0}");
        Controls.Fact(screenFacts, "Available", $"{Js.Screen.AvailWidth:F0} × {Js.Screen.AvailHeight:F0}");
        Controls.Fact(screenFacts, "Colour depth", $"{Js.Screen.ColorDepth:F0}-bit");
        _ratio = Controls.Fact(screenFacts, "Pixel ratio", "");
        _viewport = Controls.Fact(screenFacts, "Viewport", "");
        _orientation = Controls.Fact(screenFacts, "Orientation", "");
        _resized = _ => ShowViewport();
        Js.Window.On("resize", _resized);
        ShowViewport();

        // ---- Preferences: media queries, each one live -------------------------------------
        var preferences = Card(grid, "Preferences");
        var preferenceFacts = preferences.Add(Ui.El("dl", "facts"));
        Watch(preferenceFacts, "Colour scheme", "(prefers-color-scheme: dark)", "dark", "light");
        Watch(preferenceFacts, "Reduced motion", "(prefers-reduced-motion: reduce)", "reduce", "no preference");
        Watch(preferenceFacts, "More contrast", "(prefers-contrast: more)", "yes", "no");
        Watch(preferenceFacts, "Primary pointer", "(pointer: coarse)", "coarse (touch)", "fine (mouse)");
        Watch(preferenceFacts, "Hover", "(hover: hover)", "yes", "no");
        Watch(preferenceFacts, "Wide gamut", "(color-gamut: p3)", "P3 or wider", "sRGB");

        // ---- Network -----------------------------------------------------------------------
        var network = Card(grid, "Network");
        var networkFacts = network.Add(Ui.El("dl", "facts"));
        _online = Controls.Fact(networkFacts, "Status", "");
        if (Controls.Supports(Js.Navigator, "connection"))
        {
            var connection = NavigatorExtras.Connection;
            Controls.Fact(networkFacts, "Effective type", connection.EffectiveType.ToUpper());
            Controls.Fact(networkFacts, "Downlink", $"~{connection.Downlink} Mb/s");
            Controls.Fact(networkFacts, "Round trip", $"~{connection.Rtt} ms");
            Controls.Fact(networkFacts, "Data saver", connection.SaveData ? "on" : "off");
        }
        else
        {
            Controls.Fact(networkFacts, "Connection quality", "not reported");
        }
        _log = network.Add(Ui.Log("log info-log"));
        _connectivity = e => ShowOnline(e.Type);
        Js.Window.On("online", _connectivity);
        Js.Window.On("offline", _connectivity);
        ShowOnline("");

        // ---- Battery -----------------------------------------------------------------------
        _batteryCard = Card(grid, "Battery");
        _batteryChanged = _ => ShowBattery();
        if (Controls.Supports(Js.Navigator, "getBattery")) LoadBattery();
        else _batteryCard.Add(Controls.Notice("Not available here",
            "The Battery Status API is offered by Chromium browsers only. Firefox and Safari removed it over fingerprinting concerns."));
    }

    public override void Unmount()
    {
        Js.Window.Off("resize", _resized);
        Js.Window.Off("online", _connectivity);
        Js.Window.Off("offline", _connectivity);
        foreach (var watched in _queries) watched.Query.Off("change", watched.Listener);
        if (_battery is not null)
        {
            _battery.Off("levelchange", _batteryChanged);
            _battery.Off("chargingchange", _batteryChanged);
        }
    }

    private static HTMLElement Card(HTMLElement grid, string title)
    {
        var card = grid.Add(Ui.El("section", "info-card"));
        card.Add(Ui.El("h3", "panel-title", title));
        return card;
    }

    [JsTemplate("Intl.DateTimeFormat().resolvedOptions().timeZone")]
    private static string TimeZone() => throw new JsExternException();

    private void Watch(HTMLElement list, string label, string query, string yes, string no)
    {
        var watched = new Watched { Query = Js.MatchMedia(query) };
        watched.Value = Controls.Fact(list, label, "");
        watched.Listener = e =>
        {
            Show(watched, e.Matches, yes, no);
            watched.Value.ClassList.Add("is-changed");
            Js.SetTimeout(() => watched.Value.ClassList.Remove("is-changed"), 1200);
        };
        watched.Query.On("change", watched.Listener);
        Show(watched, watched.Query.Matches, yes, no);
        _queries.Add(watched);
    }

    private static void Show(Watched watched, bool matches, string yes, string no)
    {
        watched.Value.TextContent = matches ? yes : no;
    }

    private void ShowViewport()
    {
        _viewport.TextContent = $"{Js.InnerWidth:F0} × {Js.Window.InnerHeight:F0}";
        _ratio.TextContent = $"{Math.Round(Js.DevicePixelRatio, 2)}×";
        _orientation.TextContent = Controls.Supports(Js.Screen, "orientation")
            ? $"{Js.Screen.Orientation.Type} · {Js.Screen.Orientation.Angle:F0}°"
            : "not reported";
    }

    private void ShowOnline(string change)
    {
        var online = Js.Navigator.OnLine;
        _online.TextContent = online ? "online" : "offline";
        _online.ClassName = online ? "fact-value is-yes" : "fact-value is-no";
        if (change.Length > 0)
            Ui.Write(_log, $"{Ui.Clock()}  went {change}", change == "online" ? "ok" : "warn");
        else
            Ui.Write(_log, $"{Ui.Clock()}  {(online ? "online" : "offline")} — try DevTools' offline switch", "");
    }

    private async Task LoadBattery()
    {
        try
        {
            _battery = await NavigatorExtras.GetBattery();
            _battery.On("levelchange", _batteryChanged);
            _battery.On("chargingchange", _batteryChanged);
            ShowBattery();
        }
        catch (Exception error)
        {
            _batteryCard.Add(Controls.Notice("The battery could not be read", error.Message, "warn"));
        }
    }

    private void ShowBattery()
    {
        if (_battery is null) return;
        while (_batteryCard.ChildElementCount > 1) _batteryCard.LastElementChild!.Remove();

        var level = _battery.Level;
        var gauge = _batteryCard.Add(Ui.El("div", "info-battery"));
        var fill = gauge.Add(Ui.El("i", level < 0.2 ? "is-low" : ""));
        fill.Style.Width = $"{level * 100:F0}%";
        gauge.Add(Ui.El("span", "info-battery-value", $"{level * 100:F0}%"));

        var facts = _batteryCard.Add(Ui.El("dl", "facts"));
        Controls.Fact(facts, "Charging", _battery.Charging ? "yes" : "no");
        var remaining = _battery.Charging ? _battery.ChargingTime : _battery.DischargingTime;
        Controls.Fact(facts, _battery.Charging ? "Full in" : "Lasts", double.IsInfinity(remaining) || remaining == 0
            ? (_battery.Charging && level >= 1 ? "already full" : "unknown")
            : $"{Math.Floor(remaining / 3600):F0} h {Math.Floor(remaining % 3600 / 60):F0} min");
    }
}
