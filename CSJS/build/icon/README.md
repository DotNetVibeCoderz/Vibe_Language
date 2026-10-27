# Package icon

`icon.svg` is the source; `icon.png` (512 × 512, transparent corners) is what every NuGet package,
the template pack, and the VS Code extension ship.

The design follows the CSJS design system: **violet** is the C# source side, **amber** the
emitted JavaScript side, and the slanted gradient between them is the *seam*.

## Regenerating the PNG

The PNG is rendered by a browser so the text uses a real monospace face. From this folder:

```bash
chrome --headless=new --disable-gpu --hide-scrollbars \
  --default-background-color=00000000 --window-size=512,512 \
  --screenshot="$PWD/icon.png" "file://$PWD/render.html"
```

Any Chromium-based browser works (`msedge` on Windows). Check the result at 64 px — that is the
size nuget.org lists it at — before committing.

---

Gravicode Studios — led by Kang Fadhil.
