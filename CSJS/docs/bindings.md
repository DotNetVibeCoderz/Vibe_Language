# Binding generator

Point it at a `.d.ts` and get C# bindings out. It is the inverse of the `.d.ts` emitter, and it
reads the same type table.

```bash
cs2js bind node_modules/three/build/three.module.d.ts --module three --out src/Bindings/Three.cs
cs2js add vue
```

The output is ordinary C# using the interop attributes from [interop.md](interop.md), so code
that calls it transpiles to exactly the imports and calls the library expects. Nothing is
emitted for the bindings themselves; they only describe what already exists.

---

## What a declaration becomes

| TypeScript | C# |
| --- | --- |
| `export declare class Foo<T>` | `[JsImport("lib", "Foo")] public class Foo<T>` — constructors, properties, methods (`virtual`, so a subclass can override a lifecycle method), statics |
| `export declare function f(…)` | a static method on the module's container class, carrying its own `[JsImport("lib", "f")]` |
| `export declare const x: T` | a get-only static property with `[JsImport]` — imports are read-only bindings |
| `export declare const f: (…) => R` / `typeof g` | a static method, since it is called like one |
| `export default …` | `[JsImport("lib", Default = true)]`, named after the declaration |
| interface with only data (or optional callbacks) | `[JsObjectLiteral]` class with settable properties; optional methods become delegate properties |
| interface with required methods | a C# `interface` with `[JsGlobal("Object")]` — it has no runtime value, so nothing is imported for it |
| interface that is only a call signature | the delegate it describes, at every use |
| interface + `declare var X: { new(): X }` | a class, as lib.dom declares them |
| `enum` (numeric) | a C# `enum` with the library's values |
| `enum` (string) | a static class of `const string`, typed as `string` wherever it appears |
| `type A = { … }` | a `[JsObjectLiteral]` class |
| `type A<T> = …` (anything else) | substituted at every use, type arguments included |
| inline `{ … }` in a signature | a named shape, `<Owner><Member>`, deduplicated by structure |
| `namespace N { … }` exported | a static class `[JsImport("lib", "N")]`; its types are flattened into the C# namespace |
| `declare module "x" { … }` | bound as module `x` |
| `export = X` + `export as namespace X` | module bindings by default; `--global` binds them through `X.*` |
| a script with no imports or exports | `[JsGlobal]` bindings, the `Timers` pattern from `CSJS.Browser` |
| `export * from` / `export { a as b } from` | followed, across files and into other packages, and bound under the entry module's specifier |

### Types

Types cross back through `TypeMap.FromTypeScript`, the inverse half of the table
`TypeMap.ToTypeScript` reads. `BindingGeneratorTests.FromTypeScriptRoundTripsThroughToTypeScript`
takes every vocabulary entry from TypeScript to C# and back, and checks it lands where it began.

| TypeScript | C# |
| --- | --- |
| `string`, `number`, `boolean`, `bigint` | `string`, `double`, `bool`, `long` |
| `any`, `unknown`, `object` | `object` |
| `T[]`, `Array<T>`, `ReadonlyArray<T>` | `T[]` |
| `Promise<T>` / `Promise<void>` | `Task<T>` / `Task` |
| `Map<K, V>`, `Set<T>` | `Dictionary<K, V>`, `HashSet<T>` — they *are* a `Map` and a `Set` at runtime |
| `Date` | `DateTime` |
| `[A, B]` | `(A, B)`, which the emitter renders as an array |
| `(a: A) => void` / `(a: A) => R` | `Action<A>` / `Func<A, R>` |
| `T \| null`, `x?: T` | `T?` |
| `"a" \| "b"` | `string` |
| `Record<K, V>`, index-signature objects | `JsObject` — a plain object, not a `Map` |
| `HTMLElement`, `Event`, `MouseEvent`, … | the bindings that ship in `CSJS.Browser`, found by reflecting over `[JsGlobal]` |

`number` becomes `double` because that loses nothing; an `int` argument converts to it
implicitly.

### Unions

A union in a **parameter** becomes overloads — JavaScript dispatches on what it is given, so
`render(scene: Vector2 | Vector2[])` really is two signatures, and both emit `render(…)`.
Two shapes get special handling:

- `T | (() => T)` — a value or a factory, as in React's `useState` — is two overloads.
- `() => void | (() => void)` — a callback that may return a cleanup — is `Action` and
  `Func<Action>`, which is exactly how C# lambdas tell them apart.

Unions wider than four alternatives (React's `ReactNode`), rest parameters, and combinations
beyond twelve overloads map to one type instead. Elsewhere a union maps to its common type, its
value form (`T | (() => T)` → `T`), its synchronous form (`T | Promise<T>` → `T`), or `object`.

### What has no C# equivalent

Every approximation is a `// note:` beside the member, every omission a `// skipped:` — the
generator never drops anything silently. `cs2js` prints the counts.

| Construct | Treatment |
| --- | --- |
| conditional type `A extends B ? X : Y` | mapped through its general branch (`Y`, or `X` when `Y` is `never`), with a note |
| `keyof T` | `string` |
| `T["key"]` on a known object type | the member's type; anything else falls back |
| mapped types, `infer`, `typeof import(…)` | `object` where C# supplies the value, `JsObject` where C# receives it |
| intersections | the one meaningful part, or the library's own named type, with a note |
| generic callbacks | type parameters erased to `object` |
| a method type parameter no parameter mentions | erased to `object` — C# could never infer it |
| a type parameter constrained to `keyof …` / `string` | `string` |
| computed members (`[Symbol.iterator]`), unparsable statements | skipped, with the reason |
| private members | left out: they are not part of the API |

### Names

Members are PascalCase, relying on the automatic camelCase every binding member gets.
`[JsName]` appears only where the JavaScript name is not the camelCase of the C# one —
`ZERO`, `$el`, `data-id` — and on every `[JsObjectLiteral]` property, because object literals
have no automatic camelCase. Keywords are escaped (`@event`); clashes are renamed and carry
`[JsName]` (a static `create` beside an instance `create` gives `Create` and `Create_`); members
that hide inherited ones get `new`. Type names that would shadow what the generated file uses —
`Action`, `Task`, `Element`, `JsObject` — are prefixed with their namespace (`JSX.Element` →
`JSXElement`).

---

## Engine changes that came with it

Bindings exercised parts of the emitter that hand-written ones never had:

- **Binding overloads share one JavaScript name.** They were being suffixed `$1`, `$2` like
  user overloads, which called methods the library does not have.
- **Binding member names are not keyword-escaped.** `map.delete(k)` and `{ default: 1 }` are
  valid JavaScript; `delete_` named a member that does not exist.
- **A static member with its own `[JsImport]` imports itself on access**, not only on
  invocation — so `Lib.Version` emits the import binding, and a function passed as a value works.
- **A binding's indexer is bracket access.** It was emitting `.$get(…)`, the convention for
  user-declared indexers.
- **`ToTypeScript` renders tuples as tuples** (`[number, string]`), matching what the emitter
  produces and what the inverse reads.

---

## `cs2js add`: where the dependency is recorded

`cs2js add vue` writes `src/Bindings/Vue.cs` and records the package in `csjs.json`:

```json
"dependencies": {
  "vue": "3.5.44"
}
```

`csjs.json` is the project's own manifest — the file that already says what the project is and
how it builds — so it is where a dependency belongs, the way `package.json` holds npm's. The
version is pinned exactly, because the bindings were generated from exactly that version's
typings; regenerating against another version is a deliberate `cs2js add vue@x.y.z`.

What the record does not do is fetch the JavaScript. The emitted code imports `'vue'` by bare
specifier: a bundler resolves that from `node_modules`, and a page served as-is needs an import
map, which `add` prints:

```html
<script type="importmap">{ "imports": { "vue": "https://esm.sh/vue@3.5.44" } }</script>
```

Writing the import map into the page automatically is a natural next step, once `serve` knows
how to inject one.

---

## Framework bindings

`bindings/` holds bindings for React, Vue, and Angular, generated by `cs2js add` from each
framework's published typings and not edited by hand. Copy the file into a project's sources, or
run `cs2js add` to regenerate it.

| Framework | Generated from | Size | Verified |
| --- | --- | --- | --- |
| React | `@types/react@19.3.0` (for `react@19.3.1`) | 150 types, 2,700 members | server-rendered with `react-dom/server`; mounted with `react-dom/client` under jsdom — state, click → re-render, effects, and cleanup |
| Vue | `vue@3.5.44` and the `@vue/*` packages it re-exports | 472 types, 4,530 members | mounted with `createApp` under jsdom — `ref`, `computed`, `onMounted`, `h`, click → re-render |
| Angular | `@angular/core@22.2.3`, `@angular/platform-browser@22.2.3` | 544 types, 3,174 members | JIT-compiled with `@angular/compiler` and bootstrapped under jsdom — a signal-driven template, click → update |

`FrameworkBindingsTests` compiles each file, with warnings as errors, together with the same
components that were run, and checks the emitted JavaScript.

### React

Close to what a JavaScript developer writes:

```csharp
var (count, setCount) = React.UseState(0);
React.UseEffect(() => Console.WriteLine($"count is {count}"), new object[] { count });
return React.CreateElement("button",
    new InputHTMLAttributes<Element> { OnClick = _ => setCount(count + 1) }, $"{count}");
```

```javascript
const [count, setCount] = useState(0);
useEffect(() => console.log(`count is ${count}`), [count]);
return createElement('button', { onClick: (_) => setCount(count + 1) }, `${count}`);
```

`ReactNode` is a ten-way recursive union, so it is `object`. Exotic components such as
`Fragment` need an explicit type argument: `React.CreateElement<object>(React.Fragment, null, …)`.

### Vue

The composition API works, with more friction than React:

- `defineComponent` needs explicit type arguments for an untyped `setup` lambda:
  `Vue.DefineComponent<object, object, object>((props, context) => …)`.
- `h` has some thirty overloads after expansion, so a call with a string type, a props object,
  and a string child is ambiguous; casting a child to `object` picks one.
- `defineComponent` returns a `JsObject` (its real type is a conditional), so passing it to
  `createApp` takes a cast.
- The options API — `ComponentOptionsBase` has seventeen type parameters, most inferred by
  conditional types — is technically bound but not something to write by hand.

### Angular

It runs, and it is the roughest of the three, for reasons that are not about the generator:

- **Decorators.** Angular is configured with `@Component`, and C# has no decorators. The
  binding exposes the decorator as a function, `Component.Call(options)(typeof(Hello))`, which
  is the plain-JavaScript form, and needs `@angular/compiler` loaded for JIT compilation.
  Ahead-of-time compilation, which production Angular relies on, reads TypeScript source and
  cannot see a C# class at all.
- **Options are passed as anonymous objects cast through `object`**, because `Component` is both
  the decorator and the options interface, and binds as a class.
- **Shared types are duplicated per package.** `platform-browser` regenerates the `@angular/core`
  types it mentions in its own namespace, so `ApplicationRef` and `Type<T>` exist twice and
  need qualifying.
- **Template names are the emitted names.** A C# field `Count` stays `Count` in the class, so the
  template says `Count()`.

### Known limits

- Types shared between packages are generated once per package, not shared.
- A type parameter mentioned in some of a union's overloads but not others stays generic in all
  of them; an explicit type argument resolves it.
- Declaration merging across modules (`declare module "x"` augmentation) is ignored when the real
  module can be found; it is only used as a stand-in when it cannot.

---

Gravicode Studios — led by Kang Fadhil.
