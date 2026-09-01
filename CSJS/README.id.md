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
# CLI. Pasang salah satu atau keduanya — bedanya hanya nama perintah.
dotnet tool install --global CSJS.Cli.Standalone   # memberi  cs2js build
dotnet tool install --global CSJS.Cli              # memberi  dotnet cs2js build

# Pustaka, untuk memakai transpiler dari kode Anda sendiri
dotnet add package CSJS.Core
```

## Memulai proyek

```bash
cs2js new HelloWorld
cd HelloWorld
cs2js serve
```

Lalu buka <http://localhost:5080>.

## CLI

| Perintah | Fungsinya |
| --- | --- |
| `cs2js new <nama>` | Membuat proyek berisi `csjs.json`, berkas sumber, dan halaman host. |
| `cs2js build [dir]` | Mentranspilasi ke direktori keluaran. |
| `cs2js run [dir\|berkas]` | Build, lalu jalankan dengan Node. |
| `cs2js watch [dir]` | Build ulang setiap kali berkas sumber berubah. |
| `cs2js serve [dir]` | Build dan sajikan lewat HTTP (`--port`, bawaan 5080). |

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

**Didukung.** Kelas, interface, struct, record, enum, generik, pewarisan dan dispatch virtual,
properti (auto maupun terhitung), indexer, event, delegate, lambda dan closure, `async`/`await`,
iterator (`yield`), LINQ (sintaks method dan sebagian besar sintaks query), pattern matching,
switch expression, tuple dan dekonstruksi, pernyataan `using`, penanganan exception dengan klausa
catch bertipe, interpolasi string dengan penentu format, serta overloading operator.

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

**Tidak didukung, dan dilaporkan alih-alih diterjemahkan keliru:** parameter `ref` dan `out` pada
method Anda sendiri (gunakan tuple sebagai gantinya), pointer dan `unsafe`, serta anggota BCL yang
belum dipetakan. Poin terakhir itu penting: anggota tak terpetakan adalah kesalahan build, bukan
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
| **[`CSJS.Cli`](src/CSJS.Cli)** | `cs2js` — build, run, watch, serve, new. |
| **[`CSJS.Testing`](src/CSJS.Testing)** | Menulis test dalam C#, menjalankannya di runtime JavaScript. |
| **[`CSJSGen`](src/CSJSGen)** | Editor desktop dengan **Jack The Code Bender**, asisten AI. |
| **[Playground](playground)** | Transpiler yang berjalan di browser lewat WebAssembly. |
| **[Ekstensi VS Code](vscode-extension)** | Pewarnaan sintaks, IntelliSense, task, jembatan debugging. |
| **[Template](templates)** | Blazor Hybrid, ASP.NET + JS, Console-to-Web. |
| **[Contoh](samples)** | Hello world, DOM, event, three.js. |

## CSJSGen

Aplikasi Avalonia yang permukaan editornya adalah Blazor: pohon berkas, **Monaco** untuk kode C#
Anda, panel properti, dan panel keluaran yang menampilkan JavaScript dan `.d.ts` hasil
transpilasi. Panel bisa disembunyikan dan diubah lebarnya; kesalahan transpiler muncul di margin
editor.

**Berkas → Proyek baru** menawarkan 21 template, dari proyek kosong sampai scene three.js.
Semuanya aplikasi yang benar-benar bisa dibangun, bukan kerangka kosong.

Di sebelahnya ada **Jack The Code Bender** — asisten berbasis Semantic Kernel dengan penyedia
yang bisa dipilih (OpenAI, Azure OpenAI, Anthropic, Gemini, DeepSeek, Ollama). Jack bisa
mengompilasi, menjalankan, dan menyunting kode yang ia tulis, karena itu semua kernel function,
bukan sekadar klaim:

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
- [Referensi CLI](docs/cli.md)
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
