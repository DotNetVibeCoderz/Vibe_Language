# DOM Manipulation

A todo list that builds and updates DOM nodes entirely from C#, through typed bindings.

Worth looking at in `dist/Program.js`:

- `Document.QuerySelector` emits `document.querySelector` — binding members camelCase automatically.
- `List<TodoItem>` is a real `Array`; `Items.Count(i => i.Done)` becomes a `filter().length`.
- Closures capture the item, so each row knows which task it belongs to.
- `(double)done / total` stays a real division, while a plain `done / total` would have truncated.

Build and open it:

```bash
cs2js serve
```

Then open <http://localhost:5080>.

---

Gravicode Studios — led by Kang Fadhil.
