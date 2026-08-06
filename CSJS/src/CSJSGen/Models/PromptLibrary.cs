namespace CSJSGen.Models;

/// <summary>
/// Starter prompts shown when a session is empty.
/// <para>
/// These do double duty: they get someone productive immediately, and they teach the CSJS
/// idioms — <c>[JsExport]</c>, <c>CSJS.Browser</c>, <c>[JsImport]</c> — by example rather than
/// by making people read the reference first.
/// </para>
/// </summary>
public static class PromptLibrary
{
    public static IReadOnlyList<PromptTemplate> All { get; } = new PromptTemplate[]
    {
        // ---- Getting started -------------------------------------------------------------
        new("Mulai", "Halo Dunia", """
            Buatkan aplikasi CSJS "Halo Dunia" lengkap: satu kelas C# dengan [JsExport(Global = true)]
            yang mengubah teks sebuah heading, plus file index.html yang memanggilnya.
            Jelaskan singkat cara menjalankannya dengan cs2js serve.
            """, "👋"),

        new("Mulai", "Penghitung Klik", """
            Buat penghitung klik dalam CSJS. Tombol menambah angka, tombol lain mereset ke nol,
            dan angkanya ditampilkan besar di tengah. Gunakan CSJS.Browser untuk DOM-nya.
            """, "🔢"),

        new("Mulai", "Jam Digital", """
            Buat jam digital CSJS yang memperbarui dirinya tiap detik menggunakan Timers.SetInterval,
            menampilkan jam:menit:detik dan tanggal hari ini dalam bahasa Indonesia.
            """, "🕐"),

        // ---- DOM and interaction ---------------------------------------------------------
        new("Interaksi", "Daftar Tugas", """
            Buat aplikasi daftar tugas CSJS: tambah tugas, tandai selesai, hapus, dan tampilkan
            progres sebagai persentase. Simpan datanya di localStorage lewat Window.LocalStorage
            supaya bertahan setelah halaman dimuat ulang.
            """, "✅"),

        new("Interaksi", "Form Validasi", """
            Buat form pendaftaran CSJS dengan validasi sisi klien: nama wajib diisi, email harus
            valid, kata sandi minimal 8 karakter. Tampilkan pesan kesalahan per field secara
            langsung saat pengguna mengetik.
            """, "📝"),

        new("Interaksi", "Pencarian Langsung", """
            Buat kotak pencarian CSJS yang memfilter daftar item saat diketik, dengan debounce
            300ms supaya tidak memfilter di setiap ketukan tombol. Sorot bagian teks yang cocok.
            """, "🔍"),

        new("Interaksi", "Drag and Drop", """
            Buat papan kanban sederhana dalam CSJS dengan tiga kolom (Todo, Sedang Dikerjakan,
            Selesai) dan kartu yang bisa diseret antar kolom.
            """, "🗂️"),

        // ---- Data and logic --------------------------------------------------------------
        new("Data", "Tabel Terurut", """
            Buat tabel data CSJS yang bisa diurutkan dengan klik header kolom, mendukung urutan
            naik dan turun, serta menampilkan indikator arah urutan. Gunakan LINQ untuk
            pengurutannya.
            """, "📊"),

        new("Data", "Ambil dari API", """
            Buat aplikasi CSJS yang mengambil data dari sebuah REST API dengan Http.Fetch,
            menampilkan status memuat, menangani kegagalan dengan pesan yang jelas, lalu
            merender hasilnya sebagai kartu.
            """, "🌐"),

        new("Data", "Kalkulator Statistik", """
            Buat kalkulator statistik CSJS: pengguna memasukkan deret angka, aplikasi menghitung
            rata-rata, median, modus, dan simpangan baku. Tunjukkan penggunaan LINQ untuk
            perhitungannya.
            """, "🧮"),

        new("Data", "Konversi Mata Uang", """
            Buat konverter mata uang CSJS dengan kurs statis untuk IDR, USD, EUR, JPY, dan SGD.
            Konversi berjalan dua arah dan hasilnya diformat sesuai konvensi masing-masing mata uang.
            """, "💱"),

        // ---- Visual ----------------------------------------------------------------------
        new("Visual", "Grafik Batang SVG", """
            Buat komponen grafik batang CSJS yang menggambar SVG langsung dari data C#, lengkap
            dengan sumbu, label, dan animasi saat pertama kali muncul.
            """, "📈"),

        new("Visual", "Animasi Canvas", """
            Buat animasi partikel di canvas menggunakan CSJS: partikel bergerak, memantul di tepi,
            dan menarik garis antar partikel yang berdekatan. Gunakan Window.RequestAnimationFrame.
            """, "✨"),

        new("Visual", "Scene Three.js", """
            Buat scene 3D dengan three.js dari CSJS. Buatkan juga binding [JsImport] yang
            diperlukan untuk Scene, PerspectiveCamera, WebGLRenderer, Mesh, dan material.
            Tampilkan objek berputar dengan pencahayaan yang layak.
            """, "🎲"),

        new("Visual", "Tema Gelap Terang", """
            Buat pengalih tema gelap/terang dalam CSJS yang menghormati preferensi sistem,
            menyimpan pilihan pengguna di localStorage, dan beralih dengan transisi halus.
            """, "🌗"),

        // ---- Games -----------------------------------------------------------------------
        new("Permainan", "Tebak Angka", """
            Buat permainan tebak angka CSJS: komputer memilih angka 1–100, pemain menebak, dan
            aplikasi memberi petunjuk lebih besar/lebih kecil serta menghitung jumlah tebakan.
            """, "🎯"),

        new("Permainan", "Memory Card", """
            Buat permainan mencocokkan kartu dalam CSJS dengan grid 4x4, animasi balik kartu,
            penghitung langkah, dan pesan kemenangan.
            """, "🃏"),

        new("Permainan", "Game Ular", """
            Buat permainan ular klasik dengan CSJS di atas canvas: kontrol panah, ular memanjang
            saat makan, permainan berakhir saat menabrak dinding atau diri sendiri, dan skor
            tertinggi tersimpan.
            """, "🐍"),

        // ---- Interop and advanced --------------------------------------------------------
        new("Interop", "Binding Pustaka JS", """
            Tunjukkan cara membuat binding CSJS untuk pustaka JavaScript pihak ketiga.
            Gunakan Chart.js sebagai contoh: buat kelas [JsImport] yang diperlukan lalu render
            grafik garis dari data C#.
            """, "🔌"),

        new("Interop", "Bungkus Web API", """
            Buat binding CSJS untuk Web API browser yang belum ada di CSJS.Browser — misalnya
            Notification API atau Geolocation — lalu tunjukkan penggunaannya.
            """, "🧩"),

        new("Interop", "Kode Bersama", """
            Tunjukkan cara membagi satu kelas model C# antara server ASP.NET dan kode browser
            CSJS, sehingga bentuk data di kedua sisi tidak bisa berbeda.
            """, "🔗"),

        // ---- Learning --------------------------------------------------------------------
        new("Belajar", "Jelaskan Output", """
            Saya ingin memahami hasil transpilasi. Ambil sebuah kelas C# dengan LINQ, generik,
            dan async/await, lalu tunjukkan JavaScript yang dihasilkan sambil menjelaskan
            keputusan penerjemahannya.
            """, "🔬"),

        new("Belajar", "Apa yang Didukung", """
            Jelaskan fitur C# apa saja yang bisa dan tidak bisa ditranspilasi oleh CSJS, dan
            untuk yang tidak bisa, tunjukkan pola pengganti yang setara.
            """, "📚"),

        new("Belajar", "Uji di JS Runtime", """
            Tunjukkan cara menulis unit test dalam C# dengan CSJS.Testing yang dijalankan di
            runtime JavaScript, termasuk contoh kasus di mana perilaku C# dan JavaScript berbeda.
            """, "🧪"),
    };

    /// <summary>Categories in the order they should be shown.</summary>
    public static IReadOnlyList<string> Categories { get; } =
        All.Select(t => t.Category).Distinct().ToList();

    public static IEnumerable<PromptTemplate> InCategory(string category)
        => All.Where(t => t.Category == category);

    /// <summary>A random handful, used for the empty-session suggestions.</summary>
    public static IReadOnlyList<PromptTemplate> Sample(int count)
    {
        var random = new Random();
        return All.OrderBy(_ => random.Next()).Take(count).ToList();
    }
}
