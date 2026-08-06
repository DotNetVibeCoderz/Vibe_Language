# Interop guide

Calling JavaScript from C#, and exposing C# to JavaScript.

The principle throughout: **a binding declares something that already exists.** The transpiler
emits a reference to it, never a definition. That is what keeps the output free of a wrapper
layer and lets bundlers treat CSJS output like any other module.

---

## Exposing C# to the page

```csharp
[JsExport(Global = true)]
public static class App
{
    public static void Start() { }
}
```

```javascript
export class App { static Start() { } }
globalThis.App = App;
```

Public types are exported anyway; `[JsExport]` adds the `globalThis` assignment when
`Global = true`, and lets you export a non-public type or rename the export.

---

## Binding to a module

```csharp
[JsImport("three", "Scene")]
public class Scene
{
    public Scene() => throw new JsExternException();
    public void Add(Object3D child) => throw new JsExternException();
}
```

```javascript
import { Scene } from 'three';
```

No class is emitted — `Scene` *is* the imported binding. Bodies throw because they exist only to
satisfy the C# compiler; they never run.

For a default export:

```csharp
[JsImport("canvas-confetti", Default = true)]
public static class Confetti { }
```

---

## Binding to a global

```csharp
[JsGlobal("document")]
public static class Document
{
    public static Element? QuerySelector(string selectors) => throw new JsExternException();
}
```

```javascript
document.querySelector('h1')
```

**Members of a binding type are camelCased automatically.** `QuerySelector` emits
`querySelector` with no annotation. Only reach for `[JsName]` when the JavaScript name genuinely
differs from the camelCase form:

```csharp
[JsName("innerHTML")] public string InnerHtml { get; set; }
```

### A global that is a bare function

A C# static class is only a container; `setTimeout` does not live on anything called `Timers`.
Putting `[JsGlobal]` on the *member* drops the container:

```csharp
[JsGlobal]
public static class Timers
{
    [JsGlobal("setTimeout")]
    public static int SetTimeout(Action callback, double delayMs) => throw new JsExternException();
}
```

```javascript
setTimeout(() => …, 150)
```

---

## Options objects

JavaScript libraries take options bags. `[JsObjectLiteral]` gives you a typed C# shape that
emits as a plain object, with no class anywhere:

```csharp
[JsObjectLiteral]
public class RendererOptions
{
    [JsName("antialias")] public bool Antialias { get; set; }
    [JsName("alpha")] public bool Alpha { get; set; }
}

new WebGLRenderer(new RendererOptions { Antialias = true, Alpha = true });
```

```javascript
new WebGLRenderer({ antialias: true, alpha: true })
```

---

## Escape hatches

### `[JsBody]` — a raw body for a method

For behaviour the transpiler cannot express. Parameters are in scope by name:

```csharp
[JsBody("return new Intl.NumberFormat(locale).format(value);")]
public static string FormatNumber(string locale, double value) => throw new JsExternException();
```

### `[JsTemplate]` — rewrite the call site

`[JsBody]` supplies a body for an emitted method. But a binding type emits no declaration, so
its members have nowhere to live — for those, rewrite the call site instead:

```csharp
[JsTemplate("{this}[{0}]")]
public T Get<T>(string key) => throw new JsExternException();

payload.Get<int>("id")     // payload['id']
```

Placeholders: `{this}` the receiver, `{0}`…`{n}` the arguments, `{...}` all of them.

This is the user-facing form of the transpiler's own BCL mapping table.

### `[JsIgnore]` — leave it out

For server-only code sharing a file with client code:

```csharp
[JsIgnore]
public void ServerOnly() { }
```

---

## Untyped JavaScript values

`JsObject` is for shapes with no static C# type — JSON responses, mainly:

```csharp
var response = await Http.Fetch("/api/products");
var payload = await response.Json();

var items = payload.Get<JsObject[]>("items");
foreach (var item in items)
{
    var product = new Product
    {
        Id = item.Get<int>("id"),
        Name = item.Get<string>("name"),
    };
}
```

`Get<T>` emits plain member access — `item['id']` — so there is no marshalling layer.

---

## Browser bindings that ship with CSJS

`CSJS.Browser` covers the common surface:

| Type | Covers |
| --- | --- |
| `Document` | `querySelector`, `getElementById`, `createElement`, `body`, `title`, … |
| `Element` | attributes, `classList`, `style`, `innerHTML`, events, `getBoundingClientRect` |
| `Node`, `Text` | tree manipulation |
| `Event`, `MouseEvent`, `KeyboardEvent` | event payloads |
| `Window` | `innerWidth`, `location`, `localStorage`, `requestAnimationFrame`, … |
| `Storage`, `Location` | `localStorage`, `window.location` |
| `Timers` | `setTimeout`, `setInterval`, `queueMicrotask`, awaitable `Delay` |
| `Http`, `Response` | `fetch` |
| `Json` | `JSON.stringify`, `JSON.parse` |
| `JsConsole` | `console.table`, `console.group`, … |
| `JsObject` | untyped values |

Anything not here, you declare yourself — which takes about four lines.

---

## A complete binding

From [`samples/04-threejs`](../samples/04-threejs):

```csharp
[JsImport("three", "Vector3")]
public class Vector3
{
    public Vector3() => throw new JsExternException();
    public Vector3(double x, double y, double z) => throw new JsExternException();

    [JsName("x")] public double X { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("y")] public double Y { get => throw new JsExternException(); set => throw new JsExternException(); }
    [JsName("z")] public double Z { get => throw new JsExternException(); set => throw new JsExternException(); }

    public Vector3 Set(double x, double y, double z) => throw new JsExternException();
}
```

Constructor overloads on a binding all emit `new Vector3(...)` — JavaScript dispatches on
arguments at runtime, so the static-factory rewrite used for your own classes would be wrong here.

---

Gravicode Studios — led by Kang Fadhil.
