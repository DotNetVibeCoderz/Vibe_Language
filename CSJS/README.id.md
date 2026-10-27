# CSJS

[![CI](https://github.com/DotNetVibeCoderz/Vibe_Language/actions/workflows/ci.yml/badge.svg)](https://github.com/DotNetVibeCoderz/Vibe_Language/actions/workflows/ci.yml)

**C# yang berjalan sebagai JavaScript.** Sebuah transpiler, lapisan interop, CLI, editor dengan
asisten AI, dan playground browser — agar developer .NET bisa menulis untuk web tanpa keluar
dari C#.

*[Read in English →](README.md)*

<p align="center">
  <img src="docs/images/csjsgen-main.png" alt="CSJSGen: pohon berkas, Monaco berisi C#, dan JavaScript hasil transpilasinya" width="900" />
</p>

---

## Apa yang dilakukannya

Anda menulis C#:

```csharp
using System;
using CSJS;
using CSJS.Browser;

[JsExport(Global = true)]
public static class App
{
    public static void Start()
    {
        var heading = Document.QuerySelector("h1");
        if (heading is not null)
        {
            heading.TextContent = Greet("Dunia");
        }
    }

    public static string Greet(string name) => $"Halo, {name}!";
}
```

Anda mendapat JavaScript — terbaca, berbentuk modul, dan bisa di-tree-shake:

```javascript
// Generated from Program.csjs by CSJS. Do not edit.

export class App {

  static Start() {
    const heading = document.querySelector('h1');
    if (!(heading === null)) {
      heading.textContent = App.Greet('Dunia');
    }
  }

  static Greet(name) {
    return `Halo, ${name}!`;
  }
}
globalThis.App = App;
```

Ditambah berkas `.d.ts` agar konsumen TypeScript tetap punya tipe, dan source map agar
breakpoint di C# berhenti di baris yang tepat saat berjalan di browser.

## Dibuat dengannya

Semuanya dibuat dari template di CSJSGen lalu difoto saat berjalan di browser. C# adalah
satu-satunya sumbernya.

<p align="center">
  <img src="docs/images/apps/threejs.png" alt="Scene three.js yang ditulis sepenuhnya dalam C#" width="430" />
  <img src="docs/images/apps/chart.png" alt="Grafik batang SVG yang digambar dari data C#" width="430" />
</p>
<p align="center">
  <img src="docs/images/apps/calculator.png" alt="Kalkulator yang keypad-nya dibangun dari C#" width="430" />
  <img src="docs/images/apps/canvas.png" alt="Animasi partikel di canvas" width="430" />
</p>

Selengkapnya di [galeri dokumentasi](docs/README.md#applications-built-with-csjsgen).

## Pemasangan

```bash
# Template proyek — cara tercepat memulai, tanpa perlu tool global
dotnet new install CSJS.Templates

# CLI. Pasang salah satu atau keduanya — bedanya hanya nama perintah.
dotnet tool install --global CSJS.Cli.Standalone   # memberi  cs2js build
dotnet tool install --global CSJS.Cli              # memberi  dotnet cs2js build

# Pustaka, untuk memakai transpiler dari kode Anda sendiri
dotnet add package CSJS.Core
```

## Memulai proyek

```bash
dotnet new csjs-app -n HelloWorld
cd HelloWorld
dotnet run
```

Lalu buka <http://localhost:5080>. Proyeknya `.csproj` biasa yang dibangun oleh
[`CSJS.Build`](src/CSJS.Build), jadi Visual Studio, Rider, dan VS Code memberi IntelliSense penuh
dan kesalahan CSJS muncul di daftar error IDE. Template lainnya: `csjs-lib` (pustaka npm dengan
typing `.d.ts`), `csjs-aspnet`, `csjs-blazor`, dan `csjs-console` — lihat [templates](templates).

Atau dengan CLI:

```bash
cs2js new HelloWorld
cd HelloWorld
cs2js serve        # build ulang saat disimpan; halaman dimuat ulang, CSS diganti di tempat
```

## CLI

| Perintah | Fungsinya |
| --- | --- |
| `cs2js new <nama>` | Membuat proyek berisi `csjs.json`, berkas sumber, dan halaman host. |
| `cs2js build [dir]` | Mentranspilasi ke direktori keluaran. |
| `cs2js run [dir\|berkas]` | Build, lalu jalankan dengan Node. |
| `cs2js watch [dir]` | Build ulang setiap kali berkas sumber berubah. |
| `cs2js serve [dir]` | Build dan sajikan lewat HTTP dengan live reload (`--port`, bawaan 5080). |
| `cs2js build --bundle [--minify]` | Satu `bundle.js` berisi hanya helper runtime yang dipakai, beserta source map-nya. |
| `cs2js add <paket-npm>` | Mengambil paket dan membuat binding C# dari deklarasi TypeScript-nya. |
| `cs2js bind <berkas.d.ts>` | Membuat binding C# dari berkas deklarasi. |
| `cs2js lsp` | Language server: IntelliSense C# dengan diagnostik CSJS, untuk editor apa pun yang mendukung LSP. |

Build berjalan inkremental — proyek yang tidak berubah dilewati, dan `watch` hanya mengemisi
ulang yang berubah. Stack trace Node dari `cs2js run` menunjuk ke baris C# Anda.

Setiap setelan punya nilai bawaan, jadi proyek dengan sumber di `src/` bisa dibangun tanpa
berkas konfigurasi sama sekali. Bila diperlukan, `csjs.json` berbentuk seperti ini:

```json
{
  "name": "HelloWorld",
  "source": "src",
  "output": "dist",
  "module": "esm",
  "typeDefinitions": true,
  "sourceMaps": true,
  "html": "index.html"
}
```

## C# apa saja yang didukung

**Didukung, sampai C# 14.** Kelas, interface, struct dengan semantik nilai sungguhan, record,
enum, generik, pewarisan dan dispatch virtual, properti (auto, terhitung, dan kata kunci `field`),
indexer, event, delegate, lambda dan closure, `async`/`await`, iterator, LINQ dalam sintaks method
maupun sintaks query lengkap (`join`, `let`, `group … into`), pattern matching, switch expression,
tuple dan dekonstruksi, parameter `ref` dan `out`, aritmetika `checked`/`unchecked`, extension
method dan extension member C# 14, null-conditional assignment, `using`, klausa catch bertipe,
interpolasi string dengan penentu format, serta overloading operator.

Cakupan framework meliputi string, matematika, koleksi (`List`, `Dictionary`, `HashSet`,
`Stack`, `Queue`), seluruh operator LINQ, `Regex`, `DateTime` dan `TimeSpan`,
`System.Text.Json`, `StringBuilder`, serta API `Enum`. Program utuh dijalankan di .NET dan di Node
dalam suite pengujian, dan keduanya harus mencetak hal yang sama.

**Bagaimana tipe dipetakan.** Tabel ini adalah kontraknya — emitter, penulis `.d.ts`, dan
penyedia IntelliSense membaca tabel yang sama, jadi ketiganya tidak mungkin berbeda.

| C# | JavaScript | TypeScript |
| --- | --- | --- |
| `int`, `double`, `decimal` | `number` | `number` |
| `long`, `ulong` | `BigInt` | `bigint` |
| `string`, `char` | `string` | `string` |
| `bool` | `boolean` | `boolean` |
| `T[]`, `List<T>` | `Array` | `T[]` |
| `Dictionary<K,V>` | `Map` | `Map<K, V>` |
| `HashSet<T>` | `Set` | `Set<T>` |
| `Task`, `Task<T>` | `Promise` | `Promise<T>` |
| `IEnumerable<T>` dari iterator | generator | `Generator<T>` |
| `enum` | objek beku, dua arah | const object + union type |
| `record` | kelas dengan kesetaraan nilai | class |
| `DateTime` | `Date` | `Date` |
| `TimeSpan` | milidetik | `number` |
| `struct` | kelas, disalin di mana pun C# menyalin | kelas dengan `$clone()` |

**Tidak didukung, dan dilaporkan alih-alih diterjemahkan keliru:** pointer dan `unsafe`, serta
anggota BCL yang belum dipetakan. Poin terakhir itu penting: anggota tak terpetakan adalah kesalahan build, bukan
JavaScript yang gagal saat dijalankan.

`lock` tetap dikompilasi dengan peringatan — JavaScript berutas tunggal, jadi badan blok berjalan
tetapi kuncinya tidak berarti apa pun.

## Memanggil JavaScript dari C#

Deklarasikan sebuah binding, dan transpiler memancarkan import ES sungguhan. Tanpa pembungkus,
tanpa shim:

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

| Atribut | Fungsinya |
| --- | --- |
| `[JsImport("mod", "Export")]` | Mengikat ke export modul; memancarkan `import`. |
| `[JsGlobal("nama")]` | Mengikat ke global yang sudah ada; tidak memancarkan apa pun. |
| `[JsExport(Global = true)]` | Mengekspor tipe, sekaligus menaruhnya di `globalThis`. |
| `[JsName("namaJs")]` | Mengganti nama yang dipancarkan. |
| `[JsBody("…")]` | Menyediakan badan JavaScript mentah untuk sebuah method. |
| `[JsTemplate("{this}[{0}]")]` | Menulis ulang setiap titik pemanggilan menjadi ekspresi. |
| `[JsObjectLiteral]` | Memancarkan instance sebagai `{ … }`, bukan kelas. |
| `[JsIgnore]` | Mengeluarkan deklarasi sepenuhnya. |

Anggota tipe binding otomatis di-camelCase, jadi `QuerySelector` memancarkan `querySelector`
tanpa perlu `[JsName]` di setiap anggota.

## Bagian-bagiannya

| | |
| --- | --- |
| **[`CSJS.Core`](src/CSJS.Core)** | Transpiler: front end Roslyn, emitter JavaScript, emitter `.d.ts`, source map. |
| **[`CSJS.Interop`](src/CSJS.Interop)** | Atribut interop dan binding browser bertipe. |
| **[`CSJS.Cli`](src/CSJS.Cli)** | `cs2js` — build, run, watch, serve, new, bind, add, lsp. |
| **[`CSJS.Build`](src/CSJS.Build)** | Integrasi MSBuild: `dotnet build` mentranspilasi sebuah `.csproj`. |
| **[`CSJS.LanguageService`](src/CSJS.LanguageService)** | Completion, hover, signature help, definition, dan diagnostik — dipakai bersama oleh `cs2js lsp` dan CSJSGen. |
| **[`CSJS.Testing`](src/CSJS.Testing)** | Menulis test dalam C#, menjalankannya di runtime JavaScript. |
| **[`CSJSGen`](src/CSJSGen)** | Editor desktop dengan **Jack The Code Bender**, asisten AI. |
| **[Playground](playground)** | Transpiler yang berjalan di browser lewat WebAssembly. |
| **[Ekstensi VS Code](vscode-extension)** | Language client untuk `cs2js lsp`, pewarnaan sintaks, task, jembatan debugging. |
| **[Template](templates)** | `CSJS.Templates`: aplikasi browser, pustaka npm, ASP.NET + JS, Blazor Hybrid, Console-to-Web. |
| **[Binding framework](bindings)** | React, Vue, dan Angular, dibuat dari `.d.ts`-nya oleh `cs2js add`. |
| **[Contoh](samples)** | Hello world, DOM, event, three.js. |

## CSJSGen

Aplikasi Avalonia yang permukaan editornya adalah Blazor: pohon berkas, **Monaco** untuk kode C#
Anda, panel properti, dan panel keluaran yang menampilkan JavaScript dan `.d.ts` hasil
transpilasi. Panel bisa disembunyikan dan diubah lebarnya, dan tata letaknya diingat antarsesi;
completion, hover, signature help, dan diagnostik langsung berasal dari language service yang
sama dengan `cs2js lsp`.

**Berkas → Proyek baru** menawarkan 21 template, dari proyek kosong sampai scene three.js.
Semuanya aplikasi yang benar-benar bisa dibangun, bukan kerangka kosong.

Di sebelahnya ada **Jack The Code Bender** — asisten berbasis Semantic Kernel dengan penyedia
yang bisa dipilih (OpenAI, Azure OpenAI, Anthropic, Gemini, DeepSeek, Ollama). Jack bisa
mengompilasi, menjalankan, dan menyunting kode yang ia tulis, karena itu semua kernel function,
bukan sekadar klaim — dan setiap pemanggilan tampil di percakapan sebagai langkah yang bisa
dibuka, lengkap dengan argumen dan hasilnya:

| Fungsi | Kegunaannya |
| --- | --- |
| `Csjs.compile` | Mentranspilasi dan melaporkan diagnostik sungguhan. |
| `Csjs.run` | Menjalankan hasilnya di Node dan mengembalikan keluarannya. |
| `Csjs.check_support` | Menguji apakah suatu fitur C# bisa ditranspilasi. |
| `Csjs.emit_type_definitions` | Menampilkan `.d.ts` yang dihasilkan. |
| `Csjs.write_editor` / `edit_editor` / `insert_into_editor` | Mengubah berkas yang terbuka, lalu mengompilasinya untuk memeriksa. |
| `Web.search`, `Web.scrape_page`, `Web.read_file_from_url` | Mengakses internet. |
| `Utility.current_datetime`, `calculate`, `statistics`, … | Hal-hal yang sering salah bila ditebak model. |

Menambah kemampuan berarti menambah kernel function; loop chat tidak perlu disentuh. Persona,
temperature, model, dan kunci API tersimpan di `app.config` — salin
`src/CSJSGen/app.config.template` untuk memulai, atau isi kunci lewat variabel lingkungan.

## Menguji kode hasil transpilasi

Test ditulis dalam C# dan dijalankan di Node, jadi yang diverifikasi adalah JavaScript hasil
transpilasi, bukan C# aslinya:

```csharp
[CsjsTest("pembagian bilangan bulat dipotong")]
public static void PembagianBulat()
{
    Assert.Equal(3, 7 / 2);
}
```

## Dokumentasi

Dokumentasi lengkap ada di [`docs/`](docs/):

- [Panduan memulai](docs/getting-started.md)
- [Dukungan bahasa](docs/language-support.md) — apa yang ditranspilasi, dan menjadi apa
- [Panduan interop](docs/interop.md)
- [Referensi CLI](docs/cli.md) — termasuk bundling, live reload, dan `CSJS.Build`
- [Binding](docs/bindings.md) — membuat C# dari deklarasi TypeScript
- [Language server](docs/language-server.md) — VS Code, Rider, Visual Studio, Neovim
- [Panduan CSJSGen](docs/csjsgen.md)
- [Arsitektur](docs/architecture.md)

## Membangun dari sumber

```bash
git clone https://github.com/DotNetVibeCoderz/Vibe_Language
cd Vibe_Language/CSJS
dotnet build CSJS.slnx
dotnet test tests/CSJS.Core.Tests/CSJS.Core.Tests.csproj
```

## Peta jalan

Lihat [Plan.md](Plan.md) untuk peta jalan dan [Progress.md](Progress.md) untuk status terkini.

## Lisensi

MIT.

---

Dibuat oleh **Gravicode Studios**, dipimpin oleh **Kang Fadhil**.
