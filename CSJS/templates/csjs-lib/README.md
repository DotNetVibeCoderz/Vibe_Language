# CsjsLib

A JavaScript library written in C# with [CSJS](https://github.com/DotNetVibeCoderz/Vibe_Language/tree/main/CSJS).

```bash
dotnet build      # emits dist/*.js, dist/*.d.ts, dist/*.js.map
npm test          # builds, then runs test.mjs against dist/ on Node
npm publish       # ships dist/ — package.json already points at it
```

Every public type becomes an ES module export with TypeScript declarations, so JavaScript and
TypeScript consumers get the same API your C# defines:

```js
import { Money } from 'csjs-lib-package/Money.js';
new Money(10, 'USD').Split(3);  // three Money values, remainder to the first
```

Runtime helpers are imported individually from `dist/csjs-runtime.js`, and `sideEffects: false`
lets bundlers drop whatever a consumer never calls.

---

Gravicode Studios — led by Kang Fadhil.
