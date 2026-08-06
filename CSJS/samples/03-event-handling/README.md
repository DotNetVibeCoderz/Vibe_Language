# Event Handling

A calculator driven by clicks and the keyboard. Arithmetic and state live in a class with no DOM knowledge at all — which is what makes it testable on the JavaScript runtime without a browser.

Worth looking at in `dist/Program.js`:

- A C# `event` becomes a handler array; `Changed?.Invoke(...)` becomes `forEach`.
- The `switch` on `key` keeps its `break` statements, so no section falls through.
- `Operation` is a frozen object carrying a reverse map.
- `Timers.SetTimeout` emits a bare `setTimeout` — the static class is only a container.

Build and open it:

```bash
cs2js serve
```

Then open <http://localhost:5080>.

---

Gravicode Studios — led by Kang Fadhil.
