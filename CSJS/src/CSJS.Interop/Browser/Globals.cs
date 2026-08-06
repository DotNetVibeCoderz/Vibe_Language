namespace CSJS.Browser;

/// <summary>The global <c>window</c> object.</summary>
[JsGlobal("window")]
public static class Window
{
    [JsName("innerWidth")] public static double InnerWidth => throw new JsExternException();
    [JsName("innerHeight")] public static double InnerHeight => throw new JsExternException();
    [JsName("devicePixelRatio")] public static double DevicePixelRatio => throw new JsExternException();
    [JsName("location")] public static Location Location => throw new JsExternException();
    [JsName("localStorage")] public static Storage LocalStorage => throw new JsExternException();
    [JsName("sessionStorage")] public static Storage SessionStorage => throw new JsExternException();

    public static void Alert(string message) => throw new JsExternException();
    public static bool Confirm(string message) => throw new JsExternException();
    public static string? Prompt(string message) => throw new JsExternException();
    public static void AddEventListener(string type, Action<Event> listener) => throw new JsExternException();
    public static void RemoveEventListener(string type, Action<Event> listener) => throw new JsExternException();
    public static int RequestAnimationFrame(Action<double> callback) => throw new JsExternException();
    public static void CancelAnimationFrame(int handle) => throw new JsExternException();
    public static void ScrollTo(double x, double y) => throw new JsExternException();
}

/// <summary><c>window.location</c>.</summary>
[JsGlobal("Location")]
public class Location
{
    [JsName("href")] public string Href { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("protocol")] public string Protocol => throw new JsExternException();
    [JsName("host")] public string Host => throw new JsExternException();
    [JsName("hostname")] public string Hostname => throw new JsExternException();
    [JsName("pathname")] public string Pathname => throw new JsExternException();
    [JsName("search")] public string Search => throw new JsExternException();
    [JsName("hash")] public string Hash { get => throw new JsExternException(); set => throw new JsExternException(); }

    public void Reload() => throw new JsExternException();
    public void Assign(string url) => throw new JsExternException();
    public void Replace(string url) => throw new JsExternException();
}

/// <summary><c>localStorage</c> / <c>sessionStorage</c>.</summary>
[JsGlobal("Storage")]
public class Storage
{
    [JsName("length")] public int Length => throw new JsExternException();
    public string? GetItem(string key) => throw new JsExternException();
    public void SetItem(string key, string value) => throw new JsExternException();
    public void RemoveItem(string key) => throw new JsExternException();
    public void Clear() => throw new JsExternException();
    public string? Key(int index) => throw new JsExternException();
}

/// <summary>
/// Timer globals. <c>Console.WriteLine</c> and friends map to <c>console.*</c> automatically,
/// so this type only covers what has no BCL equivalent.
/// </summary>
[JsGlobal]
public static class Timers
{
    [JsGlobal("setTimeout")] public static int SetTimeout(Action callback, double delayMs) => throw new JsExternException();
    [JsGlobal("clearTimeout")] public static void ClearTimeout(int handle) => throw new JsExternException();
    [JsGlobal("setInterval")] public static int SetInterval(Action callback, double intervalMs) => throw new JsExternException();
    [JsGlobal("clearInterval")] public static void ClearInterval(int handle) => throw new JsExternException();
    [JsGlobal("queueMicrotask")] public static void QueueMicrotask(Action callback) => throw new JsExternException();

    /// <summary>Awaitable delay built on <c>setTimeout</c>.</summary>
    [JsBody("return new Promise(r => setTimeout(r, delayMs));")]
    public static Task Delay(double delayMs) => throw new JsExternException();
}

/// <summary>The <c>console</c> object, for the members that have no <c>System.Console</c> analogue.</summary>
[JsGlobal("console")]
public static class JsConsole
{
    [JsName("log")] public static void Log(params object?[] args) => throw new JsExternException();
    [JsName("info")] public static void Info(params object?[] args) => throw new JsExternException();
    [JsName("warn")] public static void Warn(params object?[] args) => throw new JsExternException();
    [JsName("error")] public static void Error(params object?[] args) => throw new JsExternException();
    [JsName("debug")] public static void Debug(params object?[] args) => throw new JsExternException();
    [JsName("table")] public static void Table(object? data) => throw new JsExternException();
    [JsName("group")] public static void Group(string label) => throw new JsExternException();
    [JsName("groupEnd")] public static void GroupEnd() => throw new JsExternException();
    [JsName("time")] public static void Time(string label) => throw new JsExternException();
    [JsName("timeEnd")] public static void TimeEnd(string label) => throw new JsExternException();
}

/// <summary>The <c>JSON</c> global.</summary>
[JsGlobal("JSON")]
public static class Json
{
    [JsName("stringify")] public static string Stringify(object? value) => throw new JsExternException();
    [JsName("stringify")] public static string Stringify(object? value, object? replacer, int space) => throw new JsExternException();
    [JsName("parse")] public static JsObject Parse(string text) => throw new JsExternException();
}

/// <summary>
/// An untyped JavaScript value. Indexers map to property access, letting C# talk to shapes
/// that have no static type on the .NET side.
/// </summary>
[JsGlobal("Object")]
public class JsObject
{
    public object? this[string key]
    {
        get => throw new JsExternException();
        set => throw new JsExternException();
    }

    public object? this[int index]
    {
        get => throw new JsExternException();
        set => throw new JsExternException();
    }

    /// <summary>Reads a property, typed as <typeparamref name="T"/>. Emits a plain member access.</summary>
    [JsTemplate("{this}[{0}]")]
    public T Get<T>(string key) => throw new JsExternException();

    [JsTemplate("({this}[{0}] = {1})")]
    public void Set<T>(string key, T value) => throw new JsExternException();

    [JsTemplate("({0} in {this})")]
    public bool Has(string key) => throw new JsExternException();
}

/// <summary>The <c>fetch</c> API.</summary>
[JsGlobal]
public static class Http
{
    [JsGlobal("fetch")] public static Task<Response> Fetch(string url) => throw new JsExternException();
    [JsGlobal("fetch")] public static Task<Response> Fetch(string url, JsObject init) => throw new JsExternException();
}

/// <summary>A <c>fetch</c> response.</summary>
[JsGlobal("Response")]
public class Response
{
    [JsName("ok")] public bool Ok => throw new JsExternException();
    [JsName("status")] public int Status => throw new JsExternException();
    [JsName("statusText")] public string StatusText => throw new JsExternException();

    [JsName("text")] public Task<string> Text() => throw new JsExternException();
    [JsName("json")] public Task<JsObject> Json() => throw new JsExternException();
}
