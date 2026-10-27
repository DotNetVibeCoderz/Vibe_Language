# CSJS.Web

The whole web platform as typed C#: every interface in `lib.dom` — DOM, CSSOM, Canvas 2D,
WebGL and WebGPU, Web Audio, media capture and recording, Geolocation, Clipboard, Notifications,
files and drag and drop, Intersection/Resize/Mutation observers, Web Workers and
BroadcastChannel, IndexedDB and Cache Storage, Web Crypto, Web Animations, speech, WebSocket,
EventSource, WebRTC, streaming `fetch`, the Payment, Credential, and Permissions APIs, and the
rest — 1,464 types and 19,338 members, each with its MDN summary.

```csharp
using CSJS.Web;

var canvas = Dom.Query<HTMLCanvasElement>("#stage")!;
var ctx = canvas.GetContext2D();
ctx.FillStyle = "#a78bfa";
ctx.FillRect(0, 0, canvas.Width, canvas.Height);

var audio = new AudioContext();
var tone = audio.CreateOscillator();
tone.Connect(audio.Destination);
tone.Start();

await Js.Navigator.Clipboard.WriteText("copied from C#");
```

```javascript
const canvas = document.querySelector('#stage');
const ctx = canvas.getContext('2d');
ctx.fillStyle = '#a78bfa';
ctx.fillRect(0, 0, canvas.width, canvas.height);
const audio = new AudioContext();
const tone = audio.createOscillator();
tone.connect(audio.destination);
tone.start();
await navigator.clipboard.writeText('copied from C#');
```

Nothing wraps anything: each call compiles to exactly the JavaScript it names.

## What is where

| File | Contents |
| --- | --- |
| `Generated/Web.g.cs` | Generated. Every interface as a class with its real inheritance (`HTMLCanvasElement : HTMLElement : Element : Node : EventTarget`), every global on `Js` (`Js.Document`, `Js.Navigator`, `Js.Fetch`, `Js.RequestAnimationFrame`). |
| `TypedArrays.cs` | `ArrayBuffer`, `Uint8Array`, `Float32Array`, `DataView`, and the rest. They are ES built-ins, so lib.dom only refers to them. |
| `Dom.cs` | What TypeScript says with generics C# cannot: `Dom.Query<T>`, `Dom.Create<T>`, `canvas.GetContext2D()`, `target.On<MouseEvent>(…)`, `value.As<T>()`. |

`CSJS.Web` ships with the CLI, the language server, and `CSJS.Build`, so a project can use it
without adding a reference.

## Regenerating

`Web.g.cs` is the output of CSJS's own binding generator over
[`@types/web`](https://www.npmjs.com/package/@types/web) — the same declarations TypeScript ships
as `lib.dom`. The current file is from **@types/web 0.0.359**.

```bash
# fetch the declarations (any version)
npm pack @types/web && tar xzf types-web-*.tgz

cs2js bind package/index.d.ts --platform --namespace CSJS.Web --container Js \
      --out src/CSJS.Web/Generated/Web.g.cs
```

`--platform` is what makes this different from binding a library: DOM names are generated
rather than mapped onto CSJS.Interop's hand-written `Element` and `Event`, and the typed arrays
are taken from `TypedArrays.cs`. Never edit `Web.g.cs` by hand — fix the generator and regenerate.

Constructs the generator cannot express are listed at the top of the file as `// skipped:`
(mostly `[Symbol.iterator]`), and approximations are marked `// note:` beside the member.

---

Gravicode Studios — led by Kang Fadhil.
