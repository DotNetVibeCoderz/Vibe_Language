namespace CSJSGen.Models;

/// <summary>One file a template contributes, relative to the project root.</summary>
/// <param name="Path">Relative path, e.g. <c>src/Program.csjs</c>.</param>
/// <param name="Content">The file's contents.</param>
public sealed record TemplateFile(string Path, string Content);

/// <summary>A starting point offered in the New Project gallery.</summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Name">Shown on the card.</param>
/// <param name="Description">One line explaining what you get.</param>
/// <param name="Icon">A single emoji used as the card's mark.</param>
/// <param name="Category">Grouping shown in the filter row.</param>
/// <param name="Files">Everything written to disk, beyond the shared scaffolding.</param>
public sealed record ProjectTemplate(
    string Id,
    string Name,
    string Description,
    string Icon,
    string Category,
    IReadOnlyList<TemplateFile> Files);

/// <summary>
/// The project templates CSJSGen can scaffold.
/// <para>
/// Each one is a working application rather than a stub, and each demonstrates a different part
/// of CSJS — so the gallery doubles as a way to learn what the transpiler can do without reading
/// the reference first.
/// </para>
/// </summary>
public static partial class ProjectTemplates
{
    public static IReadOnlyList<ProjectTemplate> All { get; } = BuildAll();

    public static IReadOnlyList<string> Categories { get; } =
        All.Select(t => t.Category).Distinct().ToList();

    public static ProjectTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static IReadOnlyList<ProjectTemplate> BuildAll() => new[]
    {
        // ---- Blank ------------------------------------------------------------------------
        new ProjectTemplate("blank", "Kosong",
            "Satu kelas kosong dan halaman host. Titik awal paling bersih.",
            "📄", "Dasar",
            new[]
            {
                new TemplateFile("src/Program.csjs", Blank),
                new TemplateFile("index.html", Html("Proyek CSJS", "<h1>Siap</h1>", "App", "Start")),
            }),

        new ProjectTemplate("hello", "Halo Dunia",
            "Kelas C# yang mengubah teks halaman. Menunjukkan alur dasar dari C# ke DOM.",
            "👋", "Dasar",
            new[]
            {
                new TemplateFile("src/Program.csjs", Hello),
                new TemplateFile("index.html", Html("Halo Dunia",
                    "<h1>Memuat…</h1>\n    <p id=\"pesan\"></p>", "App", "Start")),
            }),

        new ProjectTemplate("counter", "Penghitung",
            "Tombol tambah, kurang, dan reset dengan state di C#.",
            "🔢", "Dasar",
            new[]
            {
                new TemplateFile("src/Program.csjs", Counter),
                new TemplateFile("index.html", Html("Penghitung",
                    "<h1 id=\"layar\">0</h1>\n    <div class=\"row\">\n" +
                    "      <button id=\"kurang\">−</button>\n" +
                    "      <button id=\"reset\">Reset</button>\n" +
                    "      <button id=\"tambah\">+</button>\n    </div>", "Penghitung", "Start")),
            }),

        // ---- Interactive ------------------------------------------------------------------
        new ProjectTemplate("todo", "Daftar Tugas",
            "Tambah, tandai selesai, hapus, dan simpan ke localStorage. Menunjukkan List<T> dan LINQ.",
            "✅", "Interaktif",
            new[]
            {
                new TemplateFile("src/Program.csjs", Todo),
                new TemplateFile("index.html", Html("Daftar Tugas",
                    "<h1>Daftar Tugas</h1>\n" +
                    "    <div class=\"row\">\n" +
                    "      <input id=\"input\" type=\"text\" placeholder=\"Tugas baru…\" />\n" +
                    "      <button id=\"tambah\">Tambah</button>\n    </div>\n" +
                    "    <ul id=\"daftar\"></ul>\n    <p id=\"ringkasan\"></p>", "DaftarTugas", "Start")),
            }),

        new ProjectTemplate("calculator", "Kalkulator",
            "Aritmetika dengan keyboard dan klik. Logikanya terpisah dari DOM sehingga bisa diuji.",
            "🧮", "Interaktif",
            new[]
            {
                new TemplateFile("src/Program.csjs", Calculator),
                new TemplateFile("index.html", Html("Kalkulator",
                    "<div id=\"layar\">0</div>\n    <div class=\"pad\" id=\"pad\"></div>",
                    "Kalkulator", "Start")),
            }),

        new ProjectTemplate("form", "Form Validasi",
            "Validasi sisi klien per field saat mengetik, dengan pesan kesalahan yang jelas.",
            "📝", "Interaktif",
            new[]
            {
                new TemplateFile("src/Program.csjs", FormValidation),
                new TemplateFile("index.html", Html("Pendaftaran",
                    "<h1>Daftar</h1>\n    <form id=\"form\" novalidate>\n" +
                    "      <label>Nama<input name=\"nama\" type=\"text\" /><small></small></label>\n" +
                    "      <label>Email<input name=\"email\" type=\"text\" /><small></small></label>\n" +
                    "      <label>Kata sandi<input name=\"sandi\" type=\"password\" /><small></small></label>\n" +
                    "      <button type=\"submit\">Kirim</button>\n    </form>\n    <p id=\"hasil\"></p>",
                    "Pendaftaran", "Start")),
            }),

        new ProjectTemplate("search", "Pencarian Langsung",
            "Filter daftar saat mengetik dengan debounce, dan sorot bagian yang cocok.",
            "🔍", "Interaktif",
            new[]
            {
                new TemplateFile("src/Program.csjs", LiveSearch),
                new TemplateFile("index.html", Html("Pencarian",
                    "<h1>Cari Bahasa</h1>\n    <input id=\"cari\" type=\"text\" placeholder=\"Ketik untuk memfilter…\" />\n" +
                    "    <ul id=\"hasil\"></ul>", "Pencarian", "Start")),
            }),

        // ---- Data -------------------------------------------------------------------------
        new ProjectTemplate("table", "Tabel Terurut",
            "Tabel yang bisa diurutkan lewat header kolom. Menunjukkan LINQ OrderBy dan generik.",
            "📊", "Data",
            new[]
            {
                new TemplateFile("src/Program.csjs", SortableTable),
                new TemplateFile("index.html", Html("Tabel",
                    "<h1>Produk</h1>\n    <table id=\"tabel\"><thead></thead><tbody></tbody></table>\n" +
                    "    <p id=\"ringkasan\"></p>", "Tabel", "Start")),
            }),

        new ProjectTemplate("api", "Klien API",
            "Mengambil JSON dengan Http.Fetch, menangani status memuat dan gagal, lalu merender kartu.",
            "🌐", "Data",
            new[]
            {
                new TemplateFile("src/Program.csjs", ApiClient),
                new TemplateFile("index.html", Html("Klien API",
                    "<h1>Pengguna</h1>\n    <p id=\"status\"></p>\n    <div id=\"kartu\" class=\"grid\"></div>",
                    "KlienApi", "Start")),
            }),

        new ProjectTemplate("chart", "Grafik SVG",
            "Grafik batang yang digambar sebagai SVG langsung dari data C#, lengkap dengan sumbu.",
            "📈", "Data",
            new[]
            {
                new TemplateFile("src/Program.csjs", SvgChart),
                new TemplateFile("index.html", Html("Grafik",
                    "<h1>Penjualan per Bulan</h1>\n    <svg id=\"grafik\" role=\"img\"></svg>\n" +
                    "    <button id=\"acak\">Data baru</button>", "Grafik", "Start")),
            }),

        // ---- Visual -----------------------------------------------------------------------
        new ProjectTemplate("canvas", "Animasi Canvas",
            "Partikel yang bergerak dan saling terhubung, digerakkan RequestAnimationFrame.",
            "✨", "Visual",
            new[]
            {
                new TemplateFile("src/Program.csjs", CanvasAnimation),
                new TemplateFile("src/CanvasBindings.csjs", CanvasBindings),
                new TemplateFile("index.html", Html("Partikel",
                    "<canvas id=\"kanvas\"></canvas>", "Animasi", "Start", fullBleed: true)),
            }),

        new ProjectTemplate("threejs", "Scene 3D",
            "Objek berputar dengan three.js, lengkap dengan binding [JsImport] yang diperlukan.",
            "🎲", "Visual",
            new[]
            {
                new TemplateFile("src/Program.csjs", ThreeScene),
                new TemplateFile("src/ThreeBindings.csjs", ThreeBindings),
                new TemplateFile("index.html", ThreeHtml),
            }),

        new ProjectTemplate("theme", "Pengalih Tema",
            "Tema gelap dan terang yang menghormati preferensi sistem dan menyimpan pilihan.",
            "🌗", "Visual",
            new[]
            {
                new TemplateFile("src/Program.csjs", ThemeToggle),
                new TemplateFile("index.html", Html("Tema",
                    "<h1>Pengalih Tema</h1>\n    <p>Tema mengikuti sistem, lalu mengingat pilihan Anda.</p>\n" +
                    "    <button id=\"alih\">Ganti tema</button>\n    <p id=\"status\"></p>", "Tema", "Start")),
            }),

        // ---- Games ------------------------------------------------------------------------
        new ProjectTemplate("guess", "Tebak Angka",
            "Permainan tebak angka dengan petunjuk dan penghitung percobaan.",
            "🎯", "Permainan",
            new[]
            {
                new TemplateFile("src/Program.csjs", GuessGame),
                new TemplateFile("index.html", Html("Tebak Angka",
                    "<h1>Tebak Angka</h1>\n    <p>Saya memilih angka 1–100.</p>\n" +
                    "    <div class=\"row\">\n      <input id=\"tebakan\" type=\"text\" />\n" +
                    "      <button id=\"kirim\">Tebak</button>\n      <button id=\"ulang\">Ulangi</button>\n    </div>\n" +
                    "    <p id=\"petunjuk\"></p>\n    <p id=\"riwayat\"></p>", "TebakAngka", "Start")),
            }),

        new ProjectTemplate("memory", "Kartu Memori",
            "Permainan mencocokkan kartu 4x4 dengan penghitung langkah.",
            "🃏", "Permainan",
            new[]
            {
                new TemplateFile("src/Program.csjs", MemoryGame),
                new TemplateFile("index.html", Html("Kartu Memori",
                    "<h1>Kartu Memori</h1>\n    <div id=\"papan\" class=\"papan\"></div>\n" +
                    "    <p id=\"status\"></p>\n    <button id=\"ulang\">Ulangi</button>", "Memori", "Start")),
            }),

        // ---- Console ----------------------------------------------------------------------
        new ProjectTemplate("console", "Console ke Web",
            "Program console C# yang berjalan di browser. Console.WriteLine masuk ke terminal di halaman.",
            "⌨️", "Dasar",
            new[]
            {
                new TemplateFile("src/Program.csjs", ConsoleApp),
                new TemplateFile("index.html", ConsoleHtml),
            }),

        // ---- Testing ----------------------------------------------------------------------
        new ProjectTemplate("tested", "Dengan Unit Test",
            "Logika terpisah dari DOM, plus test yang dijalankan di runtime JavaScript.",
            "🧪", "Dasar",
            new[]
            {
                new TemplateFile("src/Program.csjs", TestedLogic),
                new TemplateFile("src/Tests.csjs", TestedTests),
                new TemplateFile("index.html", Html("Konversi Suhu",
                    "<h1>Konversi Suhu</h1>\n    <div class=\"row\">\n" +
                    "      <input id=\"celsius\" type=\"text\" placeholder=\"°C\" />\n" +
                    "      <span id=\"hasil\"></span>\n    </div>", "Konversi", "Start")),
            }),

        // ---- Node ---------------------------------------------------------------------------
        // These run on Node rather than in a page. Each ships the package.json and README it
        // needs, because "it transpiles" is not the same as "it runs" for a project with
        // dependencies and a start command.

        new ProjectTemplate("electron", "Aplikasi Desktop",
            "Aplikasi desktop Electron: proses utama dan antarmuka, keduanya ditulis dalam C#.",
            "🖥️", "Node",
            new[]
            {
                new TemplateFile("src/Main.csjs", ElectronMain),
                new TemplateFile("src/Renderer.csjs", ElectronRenderer),
                new TemplateFile("index.html", ElectronHtml),
                new TemplateFile("package.json", ElectronPackage),
                new TemplateFile("README.md", ElectronReadme),
            }),

        new ProjectTemplate("webapi", "Web API",
            "API JSON di atas modul http bawaan Node. Rute, parameter, dan 404 — tanpa dependensi.",
            "🔌", "Node",
            new[]
            {
                new TemplateFile("src/Program.csjs", WebApi),
                new TemplateFile("package.json", NodePackage("webapi-csjs")),
                new TemplateFile("README.md", WebApiReadme),
            }),

        new ProjectTemplate("chat", "Obrolan Realtime",
            "Obrolan WebSocket: peladen siaran di Node, klien di browser, keduanya C#.",
            "💬", "Node",
            new[]
            {
                new TemplateFile("src/Program.csjs", ChatServer),
                new TemplateFile("src/Client.csjs", ChatClient),
                new TemplateFile("index.html", ChatHtml),
                new TemplateFile("package.json", ChatPackage),
                new TemplateFile("README.md", ChatReadme),
            }),

        new ProjectTemplate("cron", "Layanan Latar Belakang",
            "Penjadwal pekerjaan berkala: interval per pekerjaan, kegagalan terisolasi, ringkasan.",
            "⏱️", "Node",
            new[]
            {
                new TemplateFile("src/Program.csjs", CronWorker),
                new TemplateFile("package.json", NodePackage("cron-csjs")),
                new TemplateFile("README.md", CronReadme),
            }),
    };

    // ===========================================================================================
    // Node scaffolding
    //
    // A Node project needs `"type": "module"` — the emitted code is ESM, and without it Node
    // reads .js as CommonJS and fails on the first `import`.
    // ===========================================================================================

    private static string NodePackage(string name) => $$"""
        {
          "name": "{{name}}",
          "version": "0.1.0",
          "private": true,
          "type": "module",
          "scripts": {
            "build": "cs2js build .",
            "start": "cs2js run ."
          }
        }
        """;

    private const string ElectronPackage = """
        {
          "name": "electron-csjs",
          "version": "0.1.0",
          "private": true,
          "type": "module",
          "main": "dist/Main.js",
          "scripts": {
            "build": "cs2js build .",
            "start": "cs2js build . --entry && electron ."
          },
          "devDependencies": {
            "electron": "^33.0.0"
          }
        }
        """;

    private const string ChatPackage = """
        {
          "name": "chat-csjs",
          "version": "0.1.0",
          "private": true,
          "type": "module",
          "scripts": {
            "build": "cs2js build .",
            "server": "cs2js run .",
            "client": "cs2js serve ."
          },
          "dependencies": {
            "ws": "^8.18.0"
          }
        }
        """;

    private const string ElectronHtml = """
        <!doctype html>
        <html lang="id">
          <head>
            <meta charset="utf-8" />
            <title>Aplikasi CSJS</title>
            <style>
              :root { color-scheme: dark; }
              * { box-sizing: border-box; }
              body {
                margin: 0; height: 100vh; display: grid; place-items: center;
                background: #14130f; color: #f2efe9;
                font: 15px/1.6 ui-sans-serif, system-ui, sans-serif;
              }
              .kartu {
                text-align: center; padding: 40px 56px;
                background: #1e1c17; border: 1px solid #302d26; border-radius: 12px;
              }
              #nilai { font: 600 64px/1 ui-monospace, monospace; color: #a78bfa; }
              #catatan { color: #a39e94; min-height: 1.6em; }
              button {
                font: inherit; padding: 8px 18px; margin: 0 4px; cursor: pointer;
                background: #a78bfa; color: #14130f; border: 0; border-radius: 6px;
              }
              button.sekunder { background: transparent; color: #a39e94; border: 1px solid #302d26; }
            </style>
          </head>
          <body>
            <div class="kartu">
              <div id="nilai">0</div>
              <p id="catatan"></p>
              <button id="tambah">Tambah</button>
              <button id="atur-ulang" class="sekunder">Atur ulang</button>
            </div>

            <script type="module">
              import { Antarmuka } from './dist/Renderer.js';
              Antarmuka.Start();
            </script>
          </body>
        </html>
        """;

    private const string ChatHtml = """
        <!doctype html>
        <html lang="id">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Obrolan Realtime</title>
            <style>
              :root { color-scheme: light dark; --accent: #7c3aed; }
              @media (prefers-color-scheme: dark) { :root { --accent: #a78bfa; } }
              * { box-sizing: border-box; }
              body {
                margin: 0 auto; padding: 32px 20px; max-width: 640px;
                font: 15px/1.6 ui-sans-serif, system-ui, sans-serif;
              }
              h1 { font-size: 20px; }
              #riwayat {
                list-style: none; margin: 0 0 16px; padding: 12px; min-height: 280px;
                border: 1px solid color-mix(in srgb, currentColor 15%, transparent);
                border-radius: 8px; overflow-y: auto;
              }
              #riwayat li { padding: 3px 0; font-family: ui-monospace, monospace; font-size: 13px; }
              .baris { display: flex; gap: 8px; }
              input { flex: 1; font: inherit; padding: 9px 12px; border-radius: 6px;
                      border: 1px solid color-mix(in srgb, currentColor 20%, transparent);
                      background: transparent; color: inherit; }
              button { font: inherit; padding: 9px 18px; cursor: pointer; border: 0;
                       border-radius: 6px; background: var(--accent); color: #fff; }
            </style>
          </head>
          <body>
            <h1>Obrolan Realtime</h1>
            <p>Jalankan peladen dulu: <code>npm run server</code></p>

            <ul id="riwayat"></ul>

            <div class="baris">
              <input id="pesan" type="text" placeholder="Tulis pesan lalu tekan Enter…" autocomplete="off" />
              <button id="kirim">Kirim</button>
            </div>

            <script type="module">
              import { KlienObrolan } from './dist/Client.js';
              KlienObrolan.Start();
            </script>
          </body>
        </html>
        """;

    private const string ElectronReadme = """
        # Aplikasi Desktop CSJS

        Electron dengan kedua sisinya ditulis dalam C#: `src/Main.csjs` adalah proses utama yang
        membuat jendela, `src/Renderer.csjs` adalah antarmuka yang berjalan di dalamnya.

        ```bash
        npm install
        npm start
        ```

        `npm start` mentranspilasi lalu menjalankan Electron. Untuk membangun saja: `npm run build`.

        Proses utama dan renderer adalah dua dunia yang terpisah — yang satu punya akses Node, yang
        lain punya DOM. Itulah sebabnya keduanya file terpisah, bukan satu.

        ---

        Gravicode Studios — dipimpin oleh Kang Fadhil.
        """;

    private const string WebApiReadme = """
        # Web API CSJS

        API JSON di atas modul `http` bawaan Node. Tanpa dependensi.

        ```bash
        npm start
        ```

        | Rute | Hasil |
        | --- | --- |
        | `GET /api/buku` | Seluruh koleksi |
        | `GET /api/buku/:id` | Satu buku, atau 404 |
        | `GET /api/sehat` | Pemeriksaan kesehatan |

        ```bash
        curl http://localhost:3000/api/buku
        curl http://localhost:3000/api/buku/2
        ```

        `record Buku` menjadi kelas JavaScript biasa, jadi `JSON.stringify` mengeluarkan bentuk yang
        Anda harapkan tanpa lapisan serialisasi apa pun.

        ---

        Gravicode Studios — dipimpin oleh Kang Fadhil.
        """;

    private const string ChatReadme = """
        # Obrolan Realtime CSJS

        Peladen WebSocket di Node dan klien di browser — keduanya C#.

        ```bash
        npm install
        npm run server     # peladen di ws://localhost:8080
        npm run client     # halaman di http://localhost:5080
        ```

        Keduanya perlu berjalan bersamaan. Buka halaman di dua tab untuk melihat siaran bekerja.

        `src/Program.csjs` adalah peladennya, `src/Client.csjs` sisi browsernya.

        Peladen memakai paket `ws`; klien memakai `WebSocket` bawaan browser — dua binding berbeda
        untuk protokol yang sama, yang persis mencerminkan keadaan sebenarnya di JavaScript.

        ---

        Gravicode Studios — dipimpin oleh Kang Fadhil.
        """;

    private const string CronReadme = """
        # Layanan Latar Belakang CSJS

        Penjadwal pekerjaan berkala yang berjalan terus di Node.

        ```bash
        npm start
        ```

        Tiga pekerjaan terjadwal: denyut tiap 5 detik, pembersihan tiap 15 detik, ringkasan tiap 30
        detik. Tekan Ctrl+C untuk berhenti.

        Kegagalan satu pekerjaan tidak menghentikan yang lain — `try`/`catch` ada di dalam
        pembungkus tiap pekerjaan, bukan di sekeliling penjadwal. Itu perbedaan yang baru terasa
        saat satu pekerjaan mulai melempar exception pada jam tiga pagi.

        ---

        Gravicode Studios — dipimpin oleh Kang Fadhil.
        """;

    // ===========================================================================================
    // Shared scaffolding
    // ===========================================================================================

    /// <summary>The project file every template gets.</summary>
    public static string ConfigFor(string name) => $$"""
        {
          "name": "{{name}}",
          "source": "src",
          "output": "dist",
          "module": "esm",
          "typeDefinitions": true,
          "sourceMaps": true,
          "html": "index.html"
        }
        """;

    /// <summary>
    /// A host page with the shared stylesheet inlined.
    /// <para>
    /// Templates ship one page rather than a page plus a stylesheet, so a new project is small
    /// enough to read in one sitting.
    /// </para>
    /// </summary>
    private static string Html(string title, string body, string type, string entry, bool fullBleed = false)
        => $$"""
        <!doctype html>
        <html lang="id">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{title}}</title>
            <style>
        {{SharedStyle(fullBleed)}}
            </style>
          </head>
          <body>
            <main>
            {{body}}
            </main>

            <footer>Dibuat dengan CSJS — Gravicode Studios</footer>

            <script type="module">
              import { {{type}} } from './{{"Program.js"}}';
              {{type}}.{{entry}}();
            </script>
          </body>
        </html>
        """;

    private static string SharedStyle(bool fullBleed) => """
          :root {
            color-scheme: light dark;
            --bg: #fbfaf8; --fg: #1a1917; --muted: #6b6862;
            --accent: #7c3aed; --card: #fff; --border: #e7e3dc;
          }
          @media (prefers-color-scheme: dark) {
            :root {
              --bg: #14130f; --fg: #f2efe9; --muted: #a39e94;
              --accent: #a78bfa; --card: #1e1c17; --border: #302d26;
            }
          }
          * { box-sizing: border-box; }
          body {
            margin: 0; min-height: 100vh; padding: 3rem 1.5rem;
            display: flex; flex-direction: column; align-items: center;
            background: var(--bg); color: var(--fg);
            font-family: ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif;
          }
          main { width: min(42rem, 100%); }
          h1 { font-size: 1.75rem; letter-spacing: -0.02em; margin: 0 0 1.25rem; }
          .row { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; margin-bottom: 1rem; }
          input, select {
            font: inherit; padding: 0.55rem 0.8rem; border-radius: 0.5rem;
            border: 1px solid var(--border); background: var(--card); color: var(--fg); flex: 1;
          }
          input:focus-visible { outline: 2px solid var(--accent); outline-offset: 1px; }
          button {
            font: inherit; font-weight: 600; padding: 0.55rem 1.1rem;
            border: none; border-radius: 0.5rem; background: var(--accent); color: #fff;
            cursor: pointer; transition: filter 120ms ease, transform 90ms ease;
          }
          button:hover { filter: brightness(1.08); }
          button:active { transform: translateY(1px); }
          ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.4rem; }
          li {
            display: flex; align-items: center; gap: 0.6rem;
            padding: 0.6rem 0.8rem; border: 1px solid var(--border); border-radius: 0.5rem;
          }
          li input[type="checkbox"] { flex: none; }
          li .label { flex: 1; }
          li.done .label { text-decoration: line-through; color: var(--muted); }
          table { width: 100%; border-collapse: collapse; }
          th, td { padding: 0.5rem 0.7rem; border-bottom: 1px solid var(--border); text-align: left; }
          th { cursor: pointer; user-select: none; font-size: 0.8125rem; color: var(--muted); }
          th:hover { color: var(--accent); }
          .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(12rem, 1fr)); gap: 0.75rem; }
          .card { padding: 1rem; border: 1px solid var(--border); border-radius: 0.65rem; background: var(--card); }
          label { display: block; margin-bottom: 0.9rem; font-size: 0.875rem; color: var(--muted); }
          label input { width: 100%; margin-top: 0.3rem; }
          label small { display: block; margin-top: 0.25rem; color: #dc2626; min-height: 1rem; }
          svg { width: 100%; height: 16rem; }
          mark { background: color-mix(in oklab, var(--accent) 30%, transparent); color: inherit; }
          canvas { max-width: 100%; border: 1px solid var(--border); border-radius: 0.65rem; }

          /* The calculator. Its keypad is built in C#, so the grid has to be declared here —
             without it the buttons wrap into a single row and the thing looks broken. */
          #layar {
            font: 600 2.5rem/1.2 ui-monospace, "Cascadia Code", monospace;
            text-align: right; padding: 1rem 1.2rem; margin-bottom: 0.75rem;
            border: 1px solid var(--border); border-radius: 0.65rem; background: var(--card);
            overflow-x: auto; white-space: nowrap;
          }
          .pad { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem; }
          .pad button { padding: 1rem 0; font-size: 1.05rem; }

          /* The memory game's board, likewise built in C#. */
          /* Capped, because sixteen cards across a full-width column are the size of a hand of
             playing cards each and the board stops fitting on screen. */
          .papan {
            display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.5rem;
            max-width: 22rem; margin: 0 auto 1rem;
          }
          .kartu {
            aspect-ratio: 1; font-size: 1.5rem; width: 100%; padding: 0;
            background: var(--card); color: transparent;
            border: 1px solid var(--border);
          }
          .kartu.buka { background: var(--accent); color: #fff; }
          .kartu.cocok { background: color-mix(in oklab, var(--accent) 25%, var(--card)); color: var(--fg); }
          footer {
            margin-top: 2.5rem; padding-top: 1rem; border-top: 1px solid var(--border);
            width: min(42rem, 100%); font-size: 0.8125rem; color: var(--muted);
          }
        """ + (fullBleed
            ? "\n      body { padding: 0; }\n      main { width: 100%; height: 100vh; }\n" +
              "      canvas { display: block; width: 100%; height: 100vh; }\n" +
              "      footer { position: fixed; bottom: 0.5rem; left: 1rem; border: none; margin: 0; }\n"
            : "");
}
