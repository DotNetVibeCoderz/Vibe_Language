# Hello World

The smallest useful CSJS program: a C# class transpiled to a JavaScript module and called from plain HTML.

Worth looking at in `dist/Program.js`:

- `Greet` becomes a static method returning a template literal.
- `[JsExport(Global = true)]` produces both a named export and a `globalThis` assignment.
- Integer division truncates: `_greetCount / 2` emits `Math.trunc(...)`.
- No runtime import at all — this program needs no helpers.

Build and open it:

```bash
cs2js serve
```

Then open <http://localhost:5080>.

---

Gravicode Studios — led by Kang Fadhil.
