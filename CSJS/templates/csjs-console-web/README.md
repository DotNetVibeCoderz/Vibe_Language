# Console to Web

A C# console program running in the browser. Nothing in `src/Program.csjs` knows it is not on the desktop: the host page redirects `console.log` into an on-page terminal before calling `Main`.

Demonstrates the sieve of Eratosthenes, an iterator becoming a generator, and LINQ `GroupBy` — all as ordinary C#.

Build and open it:

```bash
cs2js serve
```

Then open <http://localhost:5080>.

---

Gravicode Studios — led by Kang Fadhil.
