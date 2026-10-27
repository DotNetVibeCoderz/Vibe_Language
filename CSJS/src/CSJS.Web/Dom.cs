namespace CSJS.Web;

// What TypeScript says with generics C# cannot express. lib.dom types createElement('canvas') as
// HTMLCanvasElement through a tag-name map, and getContext('2d') through overloads that differ only
// in return type; C# sees JsObject and an ambiguous call. These helpers say it in C# instead. Each
// is a [JsTemplate], so it compiles to exactly the call it names — no wrapper exists at runtime.

/// <summary>Typed shortcuts over <c>document</c>.</summary>
public static class Dom
{
    /// <summary><c>document.querySelector(selectors)</c>, typed.</summary>
    [JsTemplate("document.querySelector({0})")]
    public static T? Query<T>(string selectors) where T : Element => throw new JsExternException();

    /// <summary><c>document.querySelectorAll(selectors)</c> as an array, so LINQ and foreach work.</summary>
    [JsTemplate("[...document.querySelectorAll({0})]")]
    public static T[] QueryAll<T>(string selectors) where T : Element => throw new JsExternException();

    /// <summary><c>document.getElementById(id)</c>, typed.</summary>
    [JsTemplate("document.getElementById({0})")]
    public static T? ById<T>(string id) where T : Element => throw new JsExternException();

    /// <summary><c>document.createElement(tag)</c>, typed.</summary>
    [JsTemplate("document.createElement({0})")]
    public static T Create<T>(string tagName) where T : Element => throw new JsExternException();

    /// <summary>A <c>&lt;div&gt;</c> with a class name — the most common element a UI builds.</summary>
    [JsTemplate("Object.assign(document.createElement({0}), { className: {1} })")]
    public static HTMLElement Create(string tagName, string className) => throw new JsExternException();

    /// <summary><c>document.body</c>.</summary>
    [JsTemplate("document.body")]
    public static HTMLElement Body => throw new JsExternException();
}

/// <summary>Typed conveniences on elements and values.</summary>
public static class DomExtensions
{
    /// <summary>Treats a value as another binding type. Emits nothing: JavaScript has no casts.</summary>
    [JsTemplate("{this}")]
    public static T As<T>(this object value) => throw new JsExternException();

    /// <summary><c>element.querySelector(selectors)</c>, typed.</summary>
    [JsTemplate("{this}.querySelector({0})")]
    public static T? Query<T>(this Element element, string selectors) where T : Element => throw new JsExternException();

    /// <summary><c>element.querySelectorAll(selectors)</c> as an array.</summary>
    [JsTemplate("[...{this}.querySelectorAll({0})]")]
    public static T[] QueryAll<T>(this Element element, string selectors) where T : Element => throw new JsExternException();

    /// <summary><c>addEventListener</c> with the event typed as <typeparamref name="TEvent"/>.</summary>
    [JsTemplate("{this}.addEventListener({0}, {1})")]
    public static void On<TEvent>(this EventTarget target, string type, Action<TEvent> listener) where TEvent : Event => throw new JsExternException();

    /// <summary><c>addEventListener</c> with options, e.g. <c>{ passive: true }</c> or <c>{ once: true }</c>.</summary>
    [JsTemplate("{this}.addEventListener({0}, {1}, {2})")]
    public static void On<TEvent>(this EventTarget target, string type, Action<TEvent> listener, AddEventListenerOptions options) where TEvent : Event => throw new JsExternException();

    /// <summary><c>removeEventListener</c>, for a listener added with <see cref="On{TEvent}(EventTarget, string, Action{TEvent})"/>.</summary>
    [JsTemplate("{this}.removeEventListener({0}, {1})")]
    public static void Off<TEvent>(this EventTarget target, string type, Action<TEvent> listener) where TEvent : Event => throw new JsExternException();

    /// <summary><c>getContext('2d')</c>.</summary>
    [JsTemplate("{this}.getContext('2d')")]
    public static CanvasRenderingContext2D GetContext2D(this HTMLCanvasElement canvas) => throw new JsExternException();

    /// <summary><c>getContext('2d', { willReadFrequently: true })</c> — for repeated <c>getImageData</c>.</summary>
    [JsTemplate("{this}.getContext('2d', { willReadFrequently: true })")]
    public static CanvasRenderingContext2D GetContext2DForReading(this HTMLCanvasElement canvas) => throw new JsExternException();

    /// <summary><c>getContext('webgl2')</c>.</summary>
    [JsTemplate("{this}.getContext('webgl2')")]
    public static WebGL2RenderingContext? GetWebGL2(this HTMLCanvasElement canvas) => throw new JsExternException();

    /// <summary><c>getContext('webgl')</c>.</summary>
    [JsTemplate("{this}.getContext('webgl')")]
    public static WebGLRenderingContext? GetWebGL(this HTMLCanvasElement canvas) => throw new JsExternException();

    /// <summary><c>getContext('webgpu')</c>.</summary>
    [JsTemplate("{this}.getContext('webgpu')")]
    public static GPUCanvasContext? GetWebGpu(this HTMLCanvasElement canvas) => throw new JsExternException();

    /// <summary><c>getContext('2d')</c> on an offscreen canvas, e.g. inside a worker.</summary>
    [JsTemplate("{this}.getContext('2d')")]
    public static OffscreenCanvasRenderingContext2D GetContext2D(this OffscreenCanvas canvas) => throw new JsExternException();

    /// <summary>A message's payload, typed.</summary>
    [JsTemplate("{this}.data")]
    public static T Data<T>(this MessageEvent<object> message) => throw new JsExternException();
}
