# Three.js Integration

A rotating 3D scene written entirely in C#, calling three.js through `[JsImport]` bindings.

`src/ThreeBindings.csjs` declares the library surface; `src/Program.csjs` uses it. Nothing here is a shim.

Worth looking at in `dist/Program.js`:

- One `import { … } from 'three'` at the top, listing exactly what the program touches.
- `ThreeBindings.js` is nearly empty — bindings emit references, not definitions.
- `[JsObjectLiteral]` options become `{ antialias: true, alpha: true }` with no class involved.
- Constructor overloads on a binding all emit `new Mesh(...)`, since JavaScript dispatches at runtime.

three.js is loaded from a CDN via an import map, so no bundler is needed to run this page.

Build and open it:

```bash
cs2js serve
```

Then open <http://localhost:5080>.

---

Gravicode Studios — led by Kang Fadhil.
