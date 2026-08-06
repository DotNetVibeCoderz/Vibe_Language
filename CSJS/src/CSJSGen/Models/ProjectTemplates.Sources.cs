namespace CSJSGen.Models;

// The source each template writes. Kept apart from the catalogue so the list of templates stays
// readable, and so a change to one program does not churn the file that describes them all.
//
// Every one of these is transpiled by the template test in CSJS.Core.Tests, so a template that
// stops compiling fails the build rather than surfacing when someone creates a project from it.

public static partial class ProjectTemplates
{
    private const string Blank = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Dipanggil dari index.html setelah modul dimuat.</summary>
        [JsExport(Global = true)]
        public static class App
        {
            public static void Start()
            {
                Console.WriteLine("CSJS siap.");
            }
        }
        """;

    private const string Hello = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Mengubah isi halaman dari C#.</summary>
        [JsExport(Global = true)]
        public static class App
        {
            public static void Start()
            {
                var judul = Document.QuerySelector("h1");
                if (judul is not null)
                {
                    judul.TextContent = Sapa("Dunia");
                }

                var pesan = Document.GetElementById("pesan");
                if (pesan is not null)
                {
                    // Pembagian bilangan bulat dipotong, persis seperti di C#.
                    pesan.TextContent = $"7 dibagi 2 sama dengan {7 / 2}, bukan {7.0 / 2}.";
                }
            }

            public static string Sapa(string nama) => $"Halo, {nama}!";
        }
        """;

    private const string Counter = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Penghitung sederhana dengan state di C#.</summary>
        [JsExport(Global = true)]
        public static class Penghitung
        {
            private static int _nilai;

            public static void Start()
            {
                Document.GetElementById("tambah")?.AddEventListener("click", _ => Ubah(1));
                Document.GetElementById("kurang")?.AddEventListener("click", _ => Ubah(-1));
                Document.GetElementById("reset")?.AddEventListener("click", _ =>
                {
                    _nilai = 0;
                    Render();
                });

                Render();
            }

            private static void Ubah(int delta)
            {
                _nilai += delta;
                Render();
            }

            private static void Render()
            {
                var layar = Document.GetElementById("layar");
                if (layar is null) return;

                layar.TextContent = _nilai.ToString();
                layar.ClassList.Toggle("besar", _nilai >= 10);
            }
        }
        """;

    private const string Todo = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Satu tugas dalam daftar.</summary>
        public class Tugas
        {
            public int Id { get; set; }
            public string Judul { get; set; } = "";
            public bool Selesai { get; set; }
        }

        /// <summary>Daftar tugas yang bertahan di localStorage.</summary>
        [JsExport(Global = true)]
        public static class DaftarTugas
        {
            private static readonly List<Tugas> Items = new();
            private static int _idBerikut = 1;

            public static void Start()
            {
                Muat();

                var input = Document.GetElementById("input");
                Document.GetElementById("tambah")?.AddEventListener("click", _ => Tambah(input));

                input?.AddEventListener("keydown", e =>
                {
                    if (((KeyboardEvent)e).Key == "Enter") Tambah(input);
                });

                Render();
            }

            private static void Tambah(Element? input)
            {
                if (input is null) return;

                var judul = input.Value.Trim();
                if (string.IsNullOrEmpty(judul)) return;

                Items.Add(new Tugas { Id = _idBerikut++, Judul = judul });
                input.Value = "";
                Simpan();
                Render();
            }

            private static void Render()
            {
                var daftar = Document.GetElementById("daftar");
                if (daftar is null) return;

                daftar.InnerHtml = "";

                foreach (var tugas in Items)
                {
                    daftar.AppendChild(BuatBaris(tugas));
                }

                var ringkasan = Document.GetElementById("ringkasan");
                if (ringkasan is not null)
                {
                    var selesai = Items.Count(t => t.Selesai);
                    ringkasan.TextContent = Items.Count == 0
                        ? "Belum ada tugas."
                        : $"{selesai} dari {Items.Count} selesai.";
                }
            }

            private static Element BuatBaris(Tugas tugas)
            {
                var baris = Document.CreateElement("li");
                baris.ClassName = tugas.Selesai ? "done" : "";

                var kotak = Document.CreateElement("input");
                kotak.SetAttribute("type", "checkbox");
                kotak.Checked = tugas.Selesai;
                kotak.AddEventListener("change", _ =>
                {
                    tugas.Selesai = !tugas.Selesai;
                    Simpan();
                    Render();
                });

                var label = Document.CreateElement("span");
                label.ClassName = "label";
                label.TextContent = tugas.Judul;

                var hapus = Document.CreateElement("button");
                hapus.TextContent = "Hapus";
                hapus.AddEventListener("click", _ =>
                {
                    Items.Remove(tugas);
                    Simpan();
                    Render();
                });

                baris.AppendChild(kotak);
                baris.AppendChild(label);
                baris.AppendChild(hapus);
                return baris;
            }

            private static void Simpan()
            {
                // Disimpan sebagai baris "id|selesai|judul" agar tidak perlu serializer.
                var baris = Items.Select(t => $"{t.Id}|{(t.Selesai ? 1 : 0)}|{t.Judul}");
                Window.LocalStorage.SetItem("tugas", string.Join("\n", baris));
            }

            private static void Muat()
            {
                var data = Window.LocalStorage.GetItem("tugas");
                if (string.IsNullOrEmpty(data)) return;

                foreach (var baris in data.Split('\n'))
                {
                    var bagian = baris.Split('|');
                    if (bagian.Length < 3) continue;

                    var id = int.Parse(bagian[0]);
                    Items.Add(new Tugas { Id = id, Selesai = bagian[1] == "1", Judul = bagian[2] });
                    if (id >= _idBerikut) _idBerikut = id + 1;
                }
            }
        }
        """;

    private const string Calculator = """
        using System;
        using System.Collections.Generic;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Operasi yang dikenali kalkulator.</summary>
        public enum Operasi { Tidak, Tambah, Kurang, Kali, Bagi }

        /// <summary>
        /// State dan aritmetika kalkulator, tanpa pengetahuan apa pun tentang DOM.
        /// Pemisahan ini yang membuatnya bisa diuji di runtime JavaScript tanpa browser.
        /// </summary>
        public class Mesin
        {
            private double _akumulator;
            private string _entri = "0";
            private Operasi _tertunda = Operasi.Tidak;
            private bool _mulaiBaru = true;

            public string Layar => _entri;

            public event Action<string>? Berubah;

            public void Digit(string digit)
            {
                if (_mulaiBaru)
                {
                    _entri = digit == "." ? "0." : digit;
                    _mulaiBaru = false;
                }
                else if (digit == "." && _entri.Contains("."))
                {
                    return;
                }
                else
                {
                    _entri = _entri == "0" && digit != "." ? digit : _entri + digit;
                }

                Beritahu();
            }

            public void SetOperasi(Operasi operasi)
            {
                Hitung();
                _tertunda = operasi;
                _mulaiBaru = true;
                Beritahu();
            }

            public void SamaDengan()
            {
                Hitung();
                _tertunda = Operasi.Tidak;
                _mulaiBaru = true;
                Beritahu();
            }

            public void Bersih()
            {
                _akumulator = 0;
                _entri = "0";
                _tertunda = Operasi.Tidak;
                _mulaiBaru = true;
                Beritahu();
            }

            private void Hitung()
            {
                var sekarang = double.Parse(_entri);

                if (_tertunda == Operasi.Tidak)
                {
                    _akumulator = sekarang;
                    return;
                }

                _akumulator = _tertunda switch
                {
                    Operasi.Tambah => _akumulator + sekarang,
                    Operasi.Kurang => _akumulator - sekarang,
                    Operasi.Kali => _akumulator * sekarang,
                    Operasi.Bagi => sekarang == 0 ? double.NaN : _akumulator / sekarang,
                    _ => sekarang,
                };

                _entri = double.IsNaN(_akumulator) ? "Error" : _akumulator.ToString();
            }

            private void Beritahu() => Berubah?.Invoke(_entri);
        }

        /// <summary>Menghubungkan mesin ke tombol dan keyboard.</summary>
        [JsExport(Global = true)]
        public static class Kalkulator
        {
            private static readonly Mesin Alat = new();

            public static void Start()
            {
                Alat.Berubah += Tampilkan;
                BangunPad();
                PasangKeyboard();
                Tampilkan(Alat.Layar);
            }

            private static void BangunPad()
            {
                var pad = Document.GetElementById("pad");
                if (pad is null) return;

                var tombol = new[]
                {
                    "AC", "÷", "×", "−",
                    "7", "8", "9", "+",
                    "4", "5", "6", "=",
                    "1", "2", "3", "0", ".",
                };

                foreach (var label in tombol)
                {
                    var el = Document.CreateElement("button");
                    el.TextContent = label;
                    el.AddEventListener("click", _ => Tekan(label));
                    pad.AppendChild(el);
                }
            }

            private static void PasangKeyboard()
            {
                Document.AddEventListener("keydown", e =>
                {
                    var key = ((KeyboardEvent)e).Key;

                    if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
                    {
                        Alat.Digit(key);
                        return;
                    }

                    switch (key)
                    {
                        case ".": Alat.Digit("."); break;
                        case "+": Alat.SetOperasi(Operasi.Tambah); break;
                        case "-": Alat.SetOperasi(Operasi.Kurang); break;
                        case "*": Alat.SetOperasi(Operasi.Kali); break;
                        case "/": Alat.SetOperasi(Operasi.Bagi); break;
                        case "Enter": Alat.SamaDengan(); break;
                        case "Escape": Alat.Bersih(); break;
                    }
                });
            }

            private static void Tekan(string label)
            {
                switch (label)
                {
                    case "AC": Alat.Bersih(); break;
                    case "=": Alat.SamaDengan(); break;
                    case "+": Alat.SetOperasi(Operasi.Tambah); break;
                    case "−": Alat.SetOperasi(Operasi.Kurang); break;
                    case "×": Alat.SetOperasi(Operasi.Kali); break;
                    case "÷": Alat.SetOperasi(Operasi.Bagi); break;
                    default: Alat.Digit(label); break;
                }
            }

            private static void Tampilkan(string nilai)
            {
                var layar = Document.GetElementById("layar");
                if (layar is not null) layar.TextContent = nilai;
            }
        }
        """;

    private const string FormValidation = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Validasi form sisi klien, per field, saat pengguna mengetik.</summary>
        [JsExport(Global = true)]
        public static class Pendaftaran
        {
            public static void Start()
            {
                foreach (var field in new[] { "nama", "email", "sandi" })
                {
                    var input = Document.QuerySelector($"[name={field}]");
                    input?.AddEventListener("input", _ => Periksa(field));
                }

                Document.GetElementById("form")?.AddEventListener("submit", e =>
                {
                    e.PreventDefault();
                    Kirim();
                });
            }

            /// <summary>Mengembalikan pesan kesalahan, atau string kosong bila valid.</summary>
            private static string Validasi(string field, string nilai)
            {
                switch (field)
                {
                    case "nama":
                        return nilai.Trim().Length == 0 ? "Nama wajib diisi." : "";

                    case "email":
                        if (nilai.Trim().Length == 0) return "Email wajib diisi.";
                        // Cukup untuk validasi sisi klien; server tetap harus memeriksa lagi.
                        var at = nilai.IndexOf("@");
                        var titik = nilai.LastIndexOf(".");
                        return at > 0 && titik > at + 1 && titik < nilai.Length - 1
                            ? ""
                            : "Format email tidak valid.";

                    case "sandi":
                        return nilai.Length < 8 ? "Minimal 8 karakter." : "";

                    default:
                        return "";
                }
            }

            private static bool Periksa(string field)
            {
                var input = Document.QuerySelector($"[name={field}]");
                if (input is null) return false;

                var pesan = Validasi(field, input.Value);

                var label = input.ParentElement;
                var catatan = label?.QuerySelector("small");
                if (catatan is not null) catatan.TextContent = pesan;

                return pesan.Length == 0;
            }

            private static void Kirim()
            {
                var semuaValid = true;
                foreach (var field in new[] { "nama", "email", "sandi" })
                {
                    if (!Periksa(field)) semuaValid = false;
                }

                var hasil = Document.GetElementById("hasil");
                if (hasil is null) return;

                hasil.TextContent = semuaValid
                    ? "Pendaftaran diterima."
                    : "Perbaiki kesalahan di atas terlebih dahulu.";
            }
        }
        """;

    private const string LiveSearch = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Memfilter daftar saat mengetik, dengan debounce dan penyorotan.</summary>
        [JsExport(Global = true)]
        public static class Pencarian
        {
            private static readonly List<string> Data = new()
            {
                "C#", "JavaScript", "TypeScript", "Python", "Rust", "Go",
                "Kotlin", "Swift", "Ruby", "Java", "Elixir", "Haskell",
            };

            private static int _timer;

            public static void Start()
            {
                var kotak = Document.GetElementById("cari");
                kotak?.AddEventListener("input", _ =>
                {
                    // Debounce: memfilter pada tiap ketukan akan menggambar ulang terlalu sering.
                    Timers.ClearTimeout(_timer);
                    _timer = Timers.SetTimeout(() => Render(kotak.Value), 200);
                });

                Render("");
            }

            private static void Render(string kueri)
            {
                var hasil = Document.GetElementById("hasil");
                if (hasil is null) return;

                hasil.InnerHtml = "";

                var cocok = kueri.Trim().Length == 0
                    ? Data
                    : Data.Where(d => d.ToLower().Contains(kueri.ToLower())).ToList();

                if (cocok.Count == 0)
                {
                    var kosong = Document.CreateElement("li");
                    kosong.TextContent = $"Tidak ada yang cocok dengan \"{kueri}\".";
                    hasil.AppendChild(kosong);
                    return;
                }

                foreach (var item in cocok)
                {
                    var baris = Document.CreateElement("li");
                    baris.InnerHtml = Sorot(item, kueri.Trim());
                    hasil.AppendChild(baris);
                }
            }

            /// <summary>Membungkus bagian yang cocok dengan &lt;mark&gt;.</summary>
            private static string Sorot(string teks, string kueri)
            {
                if (kueri.Length == 0) return teks;

                var posisi = teks.ToLower().IndexOf(kueri.ToLower());
                if (posisi < 0) return teks;

                var awal = teks.Substring(0, posisi);
                var tengah = teks.Substring(posisi, kueri.Length);
                var akhir = teks.Substring(posisi + kueri.Length);
                return $"{awal}<mark>{tengah}</mark>{akhir}";
            }
        }
        """;

    private const string SortableTable = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Satu baris data.</summary>
        public class Produk
        {
            public string Nama { get; set; } = "";
            public string Kategori { get; set; } = "";
            public double Harga { get; set; }
            public int Stok { get; set; }
        }

        /// <summary>Tabel yang diurutkan dengan klik pada header kolom.</summary>
        [JsExport(Global = true)]
        public static class Tabel
        {
            private static readonly List<Produk> Data = new()
            {
                new Produk { Nama = "Kopi Gayo", Kategori = "kopi", Harga = 85000, Stok = 12 },
                new Produk { Nama = "Kopi Toraja", Kategori = "kopi", Harga = 92000, Stok = 5 },
                new Produk { Nama = "Teh Melati", Kategori = "teh", Harga = 35000, Stok = 30 },
                new Produk { Nama = "Teh Oolong", Kategori = "teh", Harga = 68000, Stok = 8 },
                new Produk { Nama = "Gula Aren", Kategori = "pemanis", Harga = 24000, Stok = 44 },
            };

            private static string _kolom = "Nama";
            private static bool _menurun;

            public static void Start() => Render();

            private static void Render()
            {
                var tabel = Document.GetElementById("tabel");
                if (tabel is null) return;

                var kepala = tabel.QuerySelector("thead");
                var badan = tabel.QuerySelector("tbody");
                if (kepala is null || badan is null) return;

                kepala.InnerHtml = "";
                badan.InnerHtml = "";

                var barisKepala = Document.CreateElement("tr");
                foreach (var kolom in new[] { "Nama", "Kategori", "Harga", "Stok" })
                {
                    var sel = Document.CreateElement("th");
                    sel.TextContent = kolom == _kolom
                        ? $"{kolom} {(_menurun ? "▼" : "▲")}"
                        : kolom;

                    sel.AddEventListener("click", _ => Urutkan(kolom));
                    barisKepala.AppendChild(sel);
                }
                kepala.AppendChild(barisKepala);

                foreach (var produk in Terurut())
                {
                    var baris = Document.CreateElement("tr");
                    foreach (var teks in new[]
                             {
                                 produk.Nama,
                                 produk.Kategori,
                                 $"Rp {produk.Harga}",
                                 produk.Stok.ToString(),
                             })
                    {
                        var sel = Document.CreateElement("td");
                        sel.TextContent = teks;
                        baris.AppendChild(sel);
                    }
                    badan.AppendChild(baris);
                }

                var ringkasan = Document.GetElementById("ringkasan");
                if (ringkasan is not null)
                {
                    ringkasan.TextContent = $"{Data.Count} produk · total stok {Data.Sum(p => p.Stok)}";
                }
            }

            private static List<Produk> Terurut()
            {
                // Kunci pengurutan dipilih sebagai string atau angka sesuai kolomnya.
                var terurut = _kolom switch
                {
                    "Kategori" => Data.OrderBy(p => p.Kategori).ToList(),
                    "Harga" => Data.OrderBy(p => p.Harga).ToList(),
                    "Stok" => Data.OrderBy(p => p.Stok).ToList(),
                    _ => Data.OrderBy(p => p.Nama).ToList(),
                };

                if (_menurun) terurut.Reverse();
                return terurut;
            }

            private static void Urutkan(string kolom)
            {
                if (_kolom == kolom) _menurun = !_menurun;
                else
                {
                    _kolom = kolom;
                    _menurun = false;
                }

                Render();
            }
        }
        """;

    private const string ApiClient = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading.Tasks;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Bentuk data yang dirender.</summary>
        public class Pengguna
        {
            public string Nama { get; set; } = "";
            public string Email { get; set; } = "";
            public string Kota { get; set; } = "";
        }

        /// <summary>Mengambil JSON dan merendernya, dengan status memuat dan penanganan gagal.</summary>
        [JsExport(Global = true)]
        public static class KlienApi
        {
            private static readonly List<Pengguna> Data = new();

            public static async Task Start()
            {
                Status("Memuat…");

                try
                {
                    var respons = await Http.Fetch("https://jsonplaceholder.typicode.com/users");

                    if (!respons.Ok)
                    {
                        Status($"Gagal memuat (HTTP {respons.Status}).");
                        return;
                    }

                    var payload = await respons.Json();

                    // Respons berupa JSON tanpa tipe statis, jadi dibaca lewat JsObject.
                    var daftar = payload.Get<JsObject[]>("");
                    foreach (var item in daftar)
                    {
                        Data.Add(new Pengguna
                        {
                            Nama = item.Get<string>("name"),
                            Email = item.Get<string>("email"),
                            Kota = item.Get<JsObject>("address").Get<string>("city"),
                        });
                    }

                    Status($"{Data.Count} pengguna dimuat.");
                    Render();
                }
                catch (Exception ex)
                {
                    Status($"Terjadi kesalahan: {ex.Message}");
                }
            }

            private static void Render()
            {
                var wadah = Document.GetElementById("kartu");
                if (wadah is null) return;

                wadah.InnerHtml = "";

                foreach (var pengguna in Data.OrderBy(p => p.Nama))
                {
                    var kartu = Document.CreateElement("article");
                    kartu.ClassName = "card";

                    var nama = Document.CreateElement("strong");
                    nama.TextContent = pengguna.Nama;

                    var email = Document.CreateElement("p");
                    email.TextContent = pengguna.Email;

                    var kota = Document.CreateElement("small");
                    kota.TextContent = pengguna.Kota;

                    kartu.AppendChild(nama);
                    kartu.AppendChild(email);
                    kartu.AppendChild(kota);
                    wadah.AppendChild(kartu);
                }
            }

            private static void Status(string pesan)
            {
                var status = Document.GetElementById("status");
                if (status is not null) status.TextContent = pesan;
            }
        }
        """;

    private const string SvgChart = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Grafik batang yang digambar sebagai SVG langsung dari data C#.</summary>
        [JsExport(Global = true)]
        public static class Grafik
        {
            private static readonly string[] Bulan =
                { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Ags" };

            private static List<double> _nilai = new();

            public static void Start()
            {
                Acak();
                Document.GetElementById("acak")?.AddEventListener("click", _ =>
                {
                    Acak();
                    Gambar();
                });

                Gambar();
            }

            private static void Acak()
            {
                _nilai = new List<double>();
                for (var i = 0; i < Bulan.Length; i++)
                {
                    _nilai.Add(Math.Round(20 + Math.Sin(i * 1.2) * 25 + 30, 0));
                }
            }

            private static void Gambar()
            {
                var svg = Document.GetElementById("grafik");
                if (svg is null || _nilai.Count == 0) return;

                var lebar = 400.0;
                var tinggi = 160.0;
                var dasar = tinggi - 24;
                var maks = _nilai.Max();
                var lebarBatang = lebar / _nilai.Count;

                var bagian = new List<string>();

                // Garis dasar memberi grafik sesuatu untuk berpijak.
                bagian.Add($"<line x1='0' y1='{dasar}' x2='{lebar}' y2='{dasar}' " +
                           "stroke='currentColor' stroke-opacity='0.2' />");

                for (var i = 0; i < _nilai.Count; i++)
                {
                    var skala = _nilai[i] / maks;
                    // Sisakan ruang di atas batang tertinggi untuk label nilainya.
                    var tinggiBatang = skala * (dasar - 18);
                    var x = i * lebarBatang + lebarBatang * 0.18;
                    var y = dasar - tinggiBatang;
                    var w = lebarBatang * 0.64;

                    bagian.Add($"<rect x='{x}' y='{y}' width='{w}' height='{tinggiBatang}' " +
                               "rx='3' fill='var(--accent)' opacity='0.85' />");

                    bagian.Add($"<text x='{x + w / 2}' y='{y - 4}' text-anchor='middle' " +
                               $"font-size='8' fill='currentColor'>{_nilai[i]}</text>");

                    bagian.Add($"<text x='{x + w / 2}' y='{dasar + 14}' text-anchor='middle' " +
                               $"font-size='9' fill='currentColor' opacity='0.6'>{Bulan[i]}</text>");
                }

                svg.SetAttribute("viewBox", $"0 0 {lebar} {tinggi}");
                svg.InnerHtml = string.Join("", bagian);
            }
        }
        """;

    private const string CanvasBindings = """
        using CSJS;
        using CSJS.Browser;

        // Binding ke Canvas 2D. Tidak ada kelas yang dihasilkan — transpiler memancarkan
        // pemanggilan langsung ke API browser.

        /// <summary>Konteks gambar 2D.</summary>
        [JsGlobal("CanvasRenderingContext2D")]
        public class Konteks2D
        {
            [JsName("fillStyle")] public string IsiWarna { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("strokeStyle")] public string GarisWarna { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("lineWidth")] public double LebarGaris { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("globalAlpha")] public double Alpha { get => throw new JsExternException(); set => throw new JsExternException(); }

            public void ClearRect(double x, double y, double w, double h) => throw new JsExternException();
            public void FillRect(double x, double y, double w, double h) => throw new JsExternException();
            public void BeginPath() => throw new JsExternException();
            public void MoveTo(double x, double y) => throw new JsExternException();
            public void LineTo(double x, double y) => throw new JsExternException();
            public void Arc(double x, double y, double r, double mulai, double akhir) => throw new JsExternException();
            public void Fill() => throw new JsExternException();
            public void Stroke() => throw new JsExternException();
        }

        /// <summary>Elemen canvas.</summary>
        [JsGlobal("HTMLCanvasElement")]
        public class Kanvas : Element
        {
            [JsName("width")] public double Lebar { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("height")] public double Tinggi { get => throw new JsExternException(); set => throw new JsExternException(); }

            [JsName("getContext")] public Konteks2D GetContext(string jenis) => throw new JsExternException();
        }
        """;

    private const string CanvasAnimation = """
        using System;
        using System.Collections.Generic;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Satu partikel.</summary>
        public class Partikel
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Dx { get; set; }
            public double Dy { get; set; }
            public double Radius { get; set; }
        }

        /// <summary>Partikel yang memantul dan terhubung saat berdekatan.</summary>
        [JsExport(Global = true)]
        public static class Animasi
        {
            private static readonly List<Partikel> Semua = new();
            private static Kanvas? _kanvas;
            private static Konteks2D? _konteks;

            public static void Start()
            {
                _kanvas = (Kanvas?)Document.GetElementById("kanvas");
                if (_kanvas is null) return;

                _konteks = _kanvas.GetContext("2d");
                Ukur();
                Window.AddEventListener("resize", _ => Ukur());

                var acak = new Random();
                for (var i = 0; i < 70; i++)
                {
                    Semua.Add(new Partikel
                    {
                        X = acak.NextDouble() * _kanvas.Lebar,
                        Y = acak.NextDouble() * _kanvas.Tinggi,
                        Dx = (acak.NextDouble() - 0.5) * 1.4,
                        Dy = (acak.NextDouble() - 0.5) * 1.4,
                        Radius = 1.5 + acak.NextDouble() * 2,
                    });
                }

                Window.RequestAnimationFrame(Bingkai);
            }

            private static void Ukur()
            {
                if (_kanvas is null) return;
                _kanvas.Lebar = Window.InnerWidth;
                _kanvas.Tinggi = Window.InnerHeight;
            }

            private static void Bingkai(double waktu)
            {
                Window.RequestAnimationFrame(Bingkai);

                if (_kanvas is null || _konteks is null) return;

                _konteks.ClearRect(0, 0, _kanvas.Lebar, _kanvas.Tinggi);

                foreach (var p in Semua)
                {
                    p.X += p.Dx;
                    p.Y += p.Dy;

                    if (p.X < 0 || p.X > _kanvas.Lebar) p.Dx = -p.Dx;
                    if (p.Y < 0 || p.Y > _kanvas.Tinggi) p.Dy = -p.Dy;

                    _konteks.IsiWarna = "#a78bfa";
                    _konteks.BeginPath();
                    _konteks.Arc(p.X, p.Y, p.Radius, 0, Math.PI * 2);
                    _konteks.Fill();
                }

                // Garis penghubung digambar hanya untuk pasangan yang cukup dekat.
                _konteks.GarisWarna = "#a78bfa";
                _konteks.LebarGaris = 0.5;

                for (var i = 0; i < Semua.Count; i++)
                {
                    for (var j = i + 1; j < Semua.Count; j++)
                    {
                        var dx = Semua[i].X - Semua[j].X;
                        var dy = Semua[i].Y - Semua[j].Y;
                        var jarak = Math.Sqrt(dx * dx + dy * dy);

                        if (jarak > 110) continue;

                        _konteks.Alpha = 1 - jarak / 110;
                        _konteks.BeginPath();
                        _konteks.MoveTo(Semua[i].X, Semua[i].Y);
                        _konteks.LineTo(Semua[j].X, Semua[j].Y);
                        _konteks.Stroke();
                    }
                }

                _konteks.Alpha = 1;
            }
        }
        """;

    private const string ThreeBindings = """
        using CSJS;

        // Binding ke three.js. Setiap deklarasi memancarkan import ES sungguhan, bukan definisi.

        [JsImport("three", "Vector3")]
        public class Vector3
        {
            public Vector3() => throw new JsExternException();

            [JsName("x")] public double X { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("y")] public double Y { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("z")] public double Z { get => throw new JsExternException(); set => throw new JsExternException(); }

            public Vector3 Set(double x, double y, double z) => throw new JsExternException();
        }

        [JsImport("three", "Euler")]
        public class Euler
        {
            [JsName("x")] public double X { get => throw new JsExternException(); set => throw new JsExternException(); }
            [JsName("y")] public double Y { get => throw new JsExternException(); set => throw new JsExternException(); }
        }

        [JsImport("three", "Object3D")]
        public class Object3D
        {
            [JsName("position")] public Vector3 Position => throw new JsExternException();
            [JsName("rotation")] public Euler Rotation => throw new JsExternException();

            public void Add(Object3D anak) => throw new JsExternException();
        }

        [JsImport("three", "Scene")]
        public class Scene : Object3D
        {
            public Scene() => throw new JsExternException();
        }

        [JsImport("three", "PerspectiveCamera")]
        public class PerspectiveCamera : Object3D
        {
            public PerspectiveCamera(double fov, double aspect, double near, double far)
                => throw new JsExternException();

            [JsName("aspect")] public double Aspect { get => throw new JsExternException(); set => throw new JsExternException(); }
            public void UpdateProjectionMatrix() => throw new JsExternException();
        }

        [JsObjectLiteral]
        public class OpsiRenderer
        {
            [JsName("antialias")] public bool Antialias { get; set; }
            [JsName("alpha")] public bool Alpha { get; set; }
        }

        [JsImport("three", "WebGLRenderer")]
        public class WebGLRenderer
        {
            public WebGLRenderer(OpsiRenderer opsi) => throw new JsExternException();

            [JsName("domElement")] public CSJS.Browser.Element DomElement => throw new JsExternException();

            public void SetSize(double lebar, double tinggi) => throw new JsExternException();
            public void SetPixelRatio(double rasio) => throw new JsExternException();
            public void Render(Scene scene, PerspectiveCamera kamera) => throw new JsExternException();
        }

        [JsImport("three", "IcosahedronGeometry")]
        public class IcosahedronGeometry
        {
            public IcosahedronGeometry(double radius, int detail) => throw new JsExternException();
        }

        [JsObjectLiteral]
        public class OpsiMaterial
        {
            [JsName("color")] public int Warna { get; set; }
            [JsName("flatShading")] public bool FlatShading { get; set; }
            [JsName("roughness")] public double Roughness { get; set; }
        }

        [JsImport("three", "MeshStandardMaterial")]
        public class MeshStandardMaterial
        {
            public MeshStandardMaterial(OpsiMaterial opsi) => throw new JsExternException();
        }

        [JsImport("three", "Mesh")]
        public class Mesh : Object3D
        {
            public Mesh(IcosahedronGeometry geometri, MeshStandardMaterial material)
                => throw new JsExternException();
        }

        [JsImport("three", "AmbientLight")]
        public class AmbientLight : Object3D
        {
            public AmbientLight(int warna, double intensitas) => throw new JsExternException();
        }

        [JsImport("three", "PointLight")]
        public class PointLight : Object3D
        {
            public PointLight(int warna, double intensitas) => throw new JsExternException();
        }
        """;

    private const string ThreeScene = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Scene 3D yang ditulis sepenuhnya dalam C#.</summary>
        [JsExport(Global = true)]
        public static class Scene3D
        {
            private static Scene _scene = null!;
            private static PerspectiveCamera _kamera = null!;
            private static WebGLRenderer _renderer = null!;
            private static Mesh _objek = null!;
            private static double _waktu;

            public static void Start()
            {
                var wadah = Document.GetElementById("scene");
                if (wadah is null) return;

                _renderer = new WebGLRenderer(new OpsiRenderer { Antialias = true, Alpha = true });
                _renderer.SetPixelRatio(Window.DevicePixelRatio);
                _renderer.SetSize(wadah.ClientWidth, wadah.ClientHeight);
                wadah.AppendChild(_renderer.DomElement);

                _scene = new Scene();

                _kamera = new PerspectiveCamera(55, Aspek(wadah), 0.1, 100);
                _kamera.Position.Set(0, 0, 5);

                _scene.Add(new AmbientLight(0xffffff, 0.4));

                var lampu = new PointLight(0xffd9b3, 80);
                lampu.Position.Set(5, 6, 5);
                _scene.Add(lampu);

                _objek = new Mesh(
                    new IcosahedronGeometry(1.5, 1),
                    new MeshStandardMaterial(new OpsiMaterial
                    {
                        Warna = 0xa78bfa,
                        FlatShading = true,
                        Roughness = 0.4,
                    }));

                _scene.Add(_objek);

                Window.AddEventListener("resize", _ =>
                {
                    _kamera.Aspect = Aspek(wadah);
                    _kamera.UpdateProjectionMatrix();
                    _renderer.SetSize(wadah.ClientWidth, wadah.ClientHeight);
                });

                Window.RequestAnimationFrame(Bingkai);
            }

            private static double Aspek(Element wadah)
                => wadah.ClientHeight == 0 ? 1.6 : wadah.ClientWidth / wadah.ClientHeight;

            private static void Bingkai(double stempel)
            {
                Window.RequestAnimationFrame(Bingkai);

                _waktu += 0.016;
                _objek.Rotation.X = _waktu * 0.4;
                _objek.Rotation.Y = _waktu * 0.6;

                _renderer.Render(_scene, _kamera);
            }
        }
        """;

    private const string ThreeHtml = """
        <!doctype html>
        <html lang="id">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>Scene 3D</title>

            <!--
              three.js diambil dari CDN lewat import map, sehingga modul hasil transpilasi
              bisa memakai `import { Scene } from 'three'` tanpa bundler.
            -->
            <script type="importmap">
              {
                "imports": {
                  "three": "https://unpkg.com/three@0.169.0/build/three.module.js"
                }
              }
            </script>

            <style>
              :root { color-scheme: dark; }
              * { box-sizing: border-box; }
              body {
                margin: 0; min-height: 100vh; display: flex; flex-direction: column;
                background: radial-gradient(ellipse at 50% 0%, #221f19, #0d0c0a 65%);
                color: #f2efe9;
                font-family: ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif;
              }
              header { padding: 1.5rem 2rem 0; }
              h1 { margin: 0; font-size: 1.5rem; letter-spacing: -0.02em; }
              p { margin: 0.35rem 0 0; color: #a39e94; font-size: 0.875rem; }
              #scene {
                flex: 1; min-height: 24rem; margin: 1.25rem 2rem;
                border: 1px solid rgb(255 255 255 / 0.12); border-radius: 1rem; overflow: hidden;
              }
              #scene canvas { display: block; }
              footer { padding: 0 2rem 1.5rem; font-size: 0.8125rem; color: #a39e94; }
            </style>
          </head>
          <body>
            <header>
              <h1>Scene 3D dari C#</h1>
              <p>Seluruh scene ini ditulis dalam C# dan ditranspilasi ke JavaScript.</p>
            </header>

            <div id="scene"></div>

            <footer>Dibuat dengan CSJS — Gravicode Studios</footer>

            <script type="module">
              import { Scene3D } from './Program.js';
              Scene3D.Start();
            </script>
          </body>
        </html>
        """;

    private const string ThemeToggle = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Tema gelap/terang yang menghormati sistem lalu mengingat pilihan pengguna.</summary>
        [JsExport(Global = true)]
        public static class Tema
        {
            private const string Kunci = "tema";

            public static void Start()
            {
                Terapkan(TemaTersimpan());

                Document.GetElementById("alih")?.AddEventListener("click", _ =>
                {
                    var berikut = Sekarang() == "gelap" ? "terang" : "gelap";
                    Window.LocalStorage.SetItem(Kunci, berikut);
                    Terapkan(berikut);
                });
            }

            /// <summary>Pilihan tersimpan, atau preferensi sistem bila belum pernah dipilih.</summary>
            private static string TemaTersimpan()
            {
                var tersimpan = Window.LocalStorage.GetItem(Kunci);
                if (!string.IsNullOrEmpty(tersimpan)) return tersimpan;

                // Belum ada pilihan, jadi ikuti sistem.
                return SistemGelap() ? "gelap" : "terang";
            }

            [JsBody("return globalThis.matchMedia('(prefers-color-scheme: dark)').matches;")]
            private static bool SistemGelap() => throw new JsExternException();

            private static string Sekarang()
                => Document.DocumentElement.GetAttribute("data-tema") ?? "terang";

            private static void Terapkan(string tema)
            {
                Document.DocumentElement.SetAttribute("data-tema", tema);
                Document.DocumentElement.Style.SetProperty("color-scheme", tema == "gelap" ? "dark" : "light");

                var status = Document.GetElementById("status");
                if (status is not null) status.TextContent = $"Tema aktif: {tema}.";
            }
        }
        """;

    private const string GuessGame = """
        using System;
        using System.Collections.Generic;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Permainan tebak angka dengan petunjuk dan riwayat.</summary>
        [JsExport(Global = true)]
        public static class TebakAngka
        {
            private static int _rahasia;
            private static int _percobaan;
            private static readonly List<int> Riwayat = new();

            public static void Start()
            {
                Mulai();

                var input = Document.GetElementById("tebakan");
                Document.GetElementById("kirim")?.AddEventListener("click", _ => Tebak(input));
                Document.GetElementById("ulang")?.AddEventListener("click", _ => Mulai());

                input?.AddEventListener("keydown", e =>
                {
                    if (((KeyboardEvent)e).Key == "Enter") Tebak(input);
                });
            }

            private static void Mulai()
            {
                var acak = new Random();
                _rahasia = acak.Next(1, 101);
                _percobaan = 0;
                Riwayat.Clear();

                Pesan("Silakan menebak.");
                RenderRiwayat();
            }

            private static void Tebak(Element? input)
            {
                if (input is null) return;

                var teks = input.Value.Trim();
                if (!int.TryParse(teks, out var tebakan))
                {
                    Pesan("Masukkan sebuah angka.");
                    return;
                }

                if (tebakan < 1 || tebakan > 100)
                {
                    Pesan("Angka harus antara 1 dan 100.");
                    return;
                }

                _percobaan++;
                Riwayat.Add(tebakan);
                input.Value = "";

                if (tebakan == _rahasia)
                {
                    Pesan($"Tepat! {_rahasia} dalam {_percobaan} percobaan.");
                }
                else
                {
                    var arah = tebakan < _rahasia ? "lebih besar" : "lebih kecil";
                    Pesan($"{tebakan} salah — coba yang {arah}. Percobaan ke-{_percobaan}.");
                }

                RenderRiwayat();
            }

            private static void RenderRiwayat()
            {
                var riwayat = Document.GetElementById("riwayat");
                if (riwayat is null) return;

                riwayat.TextContent = Riwayat.Count == 0
                    ? ""
                    : $"Tebakan: {string.Join(", ", Riwayat)}";
            }

            private static void Pesan(string teks)
            {
                var petunjuk = Document.GetElementById("petunjuk");
                if (petunjuk is not null) petunjuk.TextContent = teks;
            }
        }
        """;

    private const string MemoryGame = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        /// <summary>Satu kartu di papan.</summary>
        public class Kartu
        {
            public int Index { get; set; }
            public string Simbol { get; set; } = "";
            public bool Terbuka { get; set; }
            public bool Cocok { get; set; }
        }

        /// <summary>Permainan mencocokkan kartu 4x4.</summary>
        [JsExport(Global = true)]
        public static class Memori
        {
            private static readonly List<Kartu> Papan = new();
            private static Kartu? _pertama;
            private static int _langkah;
            private static bool _terkunci;

            public static void Start()
            {
                Document.GetElementById("ulang")?.AddEventListener("click", _ => Susun());
                Susun();
            }

            private static void Susun()
            {
                Papan.Clear();
                _pertama = null;
                _langkah = 0;
                _terkunci = false;

                var simbol = new[] { "🍎", "🍋", "🍇", "🍑", "🥝", "🍓", "🫐", "🥥" };
                var semua = new List<string>();
                foreach (var s in simbol)
                {
                    semua.Add(s);
                    semua.Add(s);
                }

                // Pengacakan Fisher–Yates memberi distribusi yang merata.
                var acak = new Random();
                for (var i = semua.Count - 1; i > 0; i--)
                {
                    var j = acak.Next(0, i + 1);
                    var sementara = semua[i];
                    semua[i] = semua[j];
                    semua[j] = sementara;
                }

                for (var i = 0; i < semua.Count; i++)
                {
                    Papan.Add(new Kartu { Index = i, Simbol = semua[i] });
                }

                Render();
            }

            private static void Render()
            {
                var papan = Document.GetElementById("papan");
                if (papan is null) return;

                papan.InnerHtml = "";

                foreach (var kartu in Papan)
                {
                    var el = Document.CreateElement("button");
                    el.ClassName = kartu.Cocok ? "kartu cocok" : kartu.Terbuka ? "kartu buka" : "kartu";
                    el.TextContent = kartu.Terbuka || kartu.Cocok ? kartu.Simbol : "";
                    el.AddEventListener("click", _ => Buka(kartu));
                    papan.AppendChild(el);
                }

                var status = Document.GetElementById("status");
                if (status is null) return;

                var cocok = Papan.Count(k => k.Cocok);
                status.TextContent = cocok == Papan.Count
                    ? $"Selesai dalam {_langkah} langkah!"
                    : $"{_langkah} langkah · {cocok / 2} dari {Papan.Count / 2} pasang.";
            }

            private static void Buka(Kartu kartu)
            {
                if (_terkunci || kartu.Terbuka || kartu.Cocok) return;

                kartu.Terbuka = true;

                if (_pertama is null)
                {
                    _pertama = kartu;
                    Render();
                    return;
                }

                _langkah++;

                if (_pertama.Simbol == kartu.Simbol)
                {
                    _pertama.Cocok = true;
                    kartu.Cocok = true;
                    _pertama = null;
                    Render();
                    return;
                }

                // Pasangan yang salah tetap terlihat sejenak sebelum ditutup kembali.
                var sebelumnya = _pertama;
                _pertama = null;
                _terkunci = true;
                Render();

                Timers.SetTimeout(() =>
                {
                    sebelumnya.Terbuka = false;
                    kartu.Terbuka = false;
                    _terkunci = false;
                    Render();
                }, 700);
            }
        }
        """;

    private const string ConsoleApp = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;

        /// <summary>
        /// Program console C# biasa. Tidak ada yang tahu ia berjalan di browser: halaman host
        /// mengalihkan console.log ke terminal di halaman sebelum memanggil Main.
        /// </summary>
        [JsExport(Global = true)]
        public static class Program
        {
            public static void Main()
            {
                Console.WriteLine("CSJS Console — berjalan di browser.");
                Console.WriteLine();

                var prima = Saringan(50);
                Console.WriteLine($"Bilangan prima sampai 50 ({prima.Count} buah):");
                Console.WriteLine(string.Join(", ", prima));
                Console.WriteLine();

                Console.WriteLine("Deret Fibonacci:");
                foreach (var nilai in Fibonacci(12))
                {
                    Console.Write($"{nilai} ");
                }
                Console.WriteLine();
                Console.WriteLine();

                var kata = new[] { "transpiler", "csharp", "javascript", "browser", "gravicode" };
                var terpanjang = kata.OrderByDescending(k => k.Length).First();
                Console.WriteLine($"Kata terpanjang: {terpanjang} ({terpanjang.Length} huruf)");

                foreach (var grup in kata.GroupBy(k => k.Length > 7 ? "panjang" : "pendek"))
                {
                    Console.WriteLine($"  {grup.Key}: {string.Join(", ", grup)}");
                }
            }

            private static List<int> Saringan(int batas)
            {
                var komposit = new bool[batas + 1];
                var hasil = new List<int>();

                for (var n = 2; n <= batas; n++)
                {
                    if (komposit[n]) continue;

                    hasil.Add(n);
                    for (var kelipatan = n * n; kelipatan <= batas; kelipatan += n)
                    {
                        komposit[kelipatan] = true;
                    }
                }

                return hasil;
            }

            private static IEnumerable<int> Fibonacci(int jumlah)
            {
                int a = 0, b = 1;
                for (var i = 0; i < jumlah; i++)
                {
                    yield return a;
                    var berikut = a + b;
                    a = b;
                    b = berikut;
                }
            }
        }
        """;

    private const string ConsoleHtml = """
        <!doctype html>
        <html lang="id">
          <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>CSJS Console</title>
            <style>
              :root { color-scheme: dark; }
              * { box-sizing: border-box; }
              body {
                margin: 0; min-height: 100vh; padding: 2rem 1.5rem;
                background: #0d0c0a; color: #e8e4dc;
                font-family: ui-sans-serif, system-ui, -apple-system, "Segoe UI", sans-serif;
                display: grid; place-items: start center;
              }
              .shell { width: min(52rem, 100%); }
              header { display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 1rem; }
              h1 { margin: 0; font-size: 1.5rem; letter-spacing: -0.02em; }
              button {
                font: inherit; font-weight: 600; padding: 0.5rem 1.1rem;
                border: 1px solid rgb(255 255 255 / 0.12); border-radius: 0.5rem;
                background: rgb(255 255 255 / 0.06); color: inherit; cursor: pointer;
              }
              button:hover { background: rgb(255 255 255 / 0.12); }
              #terminal {
                background: #16150f; border: 1px solid rgb(255 255 255 / 0.1);
                border-radius: 0.75rem; padding: 1.25rem 1.4rem;
                min-height: 22rem; max-height: 70vh; overflow-y: auto;
                font-family: ui-monospace, "Cascadia Code", Menlo, monospace;
                font-size: 0.875rem; line-height: 1.65; white-space: pre-wrap;
              }
              .err { color: #f87171; }
              footer { margin-top: 1.25rem; font-size: 0.8125rem; color: #8a857b; }
            </style>
          </head>
          <body>
            <div class="shell">
              <header>
                <h1>Console ke Web</h1>
                <button id="ulang">Jalankan ulang</button>
              </header>

              <div id="terminal"></div>

              <footer>Dibuat dengan CSJS — Gravicode Studios</footer>
            </div>

            <script type="module">
              import { Program } from './Program.js';

              const terminal = document.getElementById('terminal');
              const nativeLog = console.log;
              const nativeError = console.error;

              // Keluaran console dialihkan ke halaman, sehingga program C# tidak perlu tahu
              // apa pun tentang browser — Console.WriteLine tetap bekerja apa adanya.
              function append(text, className) {
                const line = document.createElement('div');
                if (className) line.className = className;
                line.textContent = text;
                terminal.appendChild(line);
                terminal.scrollTop = terminal.scrollHeight;
              }

              console.log = (...args) => { append(args.map(String).join(' ')); nativeLog(...args); };
              console.error = (...args) => { append(args.map(String).join(' '), 'err'); nativeError(...args); };

              function run() {
                terminal.textContent = '';
                try { Program.Main(); }
                catch (error) { console.error(`Unhandled: ${error.message}`); }
              }

              document.getElementById('ulang').addEventListener('click', run);
              run();
            </script>
          </body>
        </html>
        """;

    private const string TestedLogic = """
        using System;
        using CSJS;
        using CSJS.Browser;

        /// <summary>
        /// Konversi suhu. Logikanya murni sehingga bisa diuji di runtime JavaScript
        /// tanpa memerlukan browser — lihat Tests.csjs.
        /// </summary>
        public static class Suhu
        {
            public static double KeFahrenheit(double celsius) => celsius * 9 / 5 + 32;

            public static double KeCelsius(double fahrenheit) => (fahrenheit - 32) * 5 / 9;

            /// <summary>Membulatkan ke satu desimal, seperti yang ditampilkan di UI.</summary>
            public static double Bulatkan(double nilai) => Math.Round(nilai, 1);
        }

        /// <summary>Menghubungkan konversi ke halaman.</summary>
        [JsExport(Global = true)]
        public static class Konversi
        {
            public static void Start()
            {
                var input = Document.GetElementById("celsius");
                input?.AddEventListener("input", _ => Perbarui(input));
                Perbarui(input);
            }

            private static void Perbarui(Element? input)
            {
                var hasil = Document.GetElementById("hasil");
                if (hasil is null) return;

                if (input is null || input.Value.Trim().Length == 0)
                {
                    hasil.TextContent = "";
                    return;
                }

                if (!double.TryParse(input.Value.Trim(), out var celsius))
                {
                    hasil.TextContent = "Bukan angka.";
                    return;
                }

                hasil.TextContent = $"= {Suhu.Bulatkan(Suhu.KeFahrenheit(celsius))} °F";
            }
        }
        """;

    private const string TestedTests = """
        using System;
        using CSJS.Testing;

        /// <summary>
        /// Test yang ditranspilasi dan dijalankan di Node, sehingga yang diverifikasi adalah
        /// JavaScript hasil transpilasi — bukan C# aslinya.
        /// </summary>
        [CsjsTestSuite("Konversi suhu")]
        public static class SuhuTests
        {
            [CsjsTest("titik beku air")]
            public static void TitikBeku()
            {
                Assert.Equal(32.0, Suhu.KeFahrenheit(0));
            }

            [CsjsTest("titik didih air")]
            public static void TitikDidih()
            {
                Assert.Equal(212.0, Suhu.KeFahrenheit(100));
            }

            [CsjsTest("konversi bolak-balik kembali ke nilai semula")]
            public static void BolakBalik()
            {
                Assert.Close(37.0, Suhu.KeCelsius(Suhu.KeFahrenheit(37)), 1e-9);
            }

            [CsjsTest("pembulatan satu desimal")]
            public static void Pembulatan()
            {
                Assert.Equal(98.6, Suhu.Bulatkan(Suhu.KeFahrenheit(37)));
            }
        }
        """;

    // =======================================================================================
    // Node-side templates
    //
    // Everything above runs in a browser. These four run on Node, which changes only where the
    // bindings point — the language, the emitter, and the type table are the same. Node's own
    // modules are reached the same way any JavaScript library is: `[JsImport]`.
    //
    // Note the `"default"` import name. Node's built-ins expose their functions on the default
    // export, so `import { default as NodeHttp } from 'node:http'` gives an object to call
    // `createServer` on. Importing the named export directly would emit a bare function call the
    // import collector does not see.
    // =======================================================================================

    private const string ElectronMain = """
        using System;
        using System.Threading.Tasks;
        using CSJS;

        // Proses utama Electron: membuat jendela dan mengelola daur hidup aplikasi.
        // Antarmukanya ada di Renderer.csjs, yang berjalan di dalam jendela ini.

        /// <summary>Opsi jendela, dipancarkan sebagai objek literal.</summary>
        [JsObjectLiteral]
        public class OpsiJendela
        {
            [JsName("width")] public int Lebar { get; set; }
            [JsName("height")] public int Tinggi { get; set; }
            [JsName("title")] public string Judul { get; set; } = "";
            [JsName("backgroundColor")] public string WarnaLatar { get; set; } = "";
        }

        [JsImport("electron", "BrowserWindow")]
        public class Jendela
        {
            public Jendela(OpsiJendela opsi) => throw new JsExternException();

            [JsName("loadFile")] public void MuatBerkas(string berkas) => throw new JsExternException();
            [JsName("show")] public void Tampilkan() => throw new JsExternException();
        }

        [JsImport("electron", "app")]
        public static class Aplikasi
        {
            [JsName("whenReady")] public static Task SiapAsync() => throw new JsExternException();
            [JsName("quit")] public static void Keluar() => throw new JsExternException();
            [JsName("on")] public static void Pada(string peristiwa, Action penangan) => throw new JsExternException();
        }

        /// <summary>Proses Node yang menjalankan Electron.</summary>
        [JsGlobal("process")]
        public static class Proses
        {
            [JsName("platform")] public static string Platform => throw new JsExternException();
        }

        [JsExport(Global = true)]
        public static class Utama
        {
            public static async Task Main()
            {
                await Aplikasi.SiapAsync();
                BuatJendela();

                // macOS menahan aplikasi tetap hidup setelah jendela terakhir ditutup;
                // platform lain tidak, jadi keduanya ditangani terpisah.
                Aplikasi.Pada("window-all-closed", () =>
                {
                    if (Proses.Platform != "darwin") Aplikasi.Keluar();
                });
            }

            private static void BuatJendela()
            {
                var jendela = new Jendela(new OpsiJendela
                {
                    Lebar = 960,
                    Tinggi = 680,
                    Judul = "Aplikasi CSJS",
                    WarnaLatar = "#14130f",
                });

                jendela.MuatBerkas("index.html");
            }
        }
        """;

    private const string ElectronRenderer = """
        using System;
        using CSJS;
        using CSJS.Browser;

        // Renderer Electron: kode yang berjalan di dalam jendela. Ini DOM biasa, jadi
        // binding browser yang sama seperti proyek web lain berlaku di sini.

        [JsExport(Global = true)]
        public static class Antarmuka
        {
            private static int _hitung;

            public static void Start()
            {
                var tombol = Document.GetElementById("tambah");
                var atur = Document.GetElementById("atur-ulang");

                tombol?.AddEventListener("click", _ =>
                {
                    _hitung++;
                    Gambar();
                });

                atur?.AddEventListener("click", _ =>
                {
                    _hitung = 0;
                    Gambar();
                });

                Gambar();
            }

            private static void Gambar()
            {
                var layar = Document.GetElementById("nilai");
                if (layar is not null) layar.TextContent = _hitung.ToString();

                var catatan = Document.GetElementById("catatan");
                if (catatan is not null)
                {
                    catatan.TextContent = _hitung == 0
                        ? "Belum ada klik."
                        : $"{_hitung} klik sejauh ini.";
                }
            }
        }
        """;

    private const string WebApi = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CSJS;
        using CSJS.Browser;

        // Web API di atas modul http bawaan Node — tanpa dependensi sama sekali.
        //
        // `CSJS.Browser` di sini hanya untuk `Json`: JSON adalah global di Node persis seperti di
        // browser, jadi binding yang sama berlaku meski namespace-nya menyebut browser.

        /// <summary>Permintaan HTTP masuk.</summary>
        [JsGlobal("Object")]
        public class Permintaan
        {
            [JsName("method")] public string Metode => throw new JsExternException();
            [JsName("url")] public string Url => throw new JsExternException();
        }

        /// <summary>Respons yang sedang disusun.</summary>
        [JsGlobal("Object")]
        public class Respons
        {
            [JsName("writeHead")] public void TulisKepala(int kode, object header) => throw new JsExternException();
            [JsName("end")] public void Selesai(string isi) => throw new JsExternException();
        }

        [JsObjectLiteral]
        public class HeaderJson
        {
            [JsName("content-type")] public string TipeKonten { get; set; } = "application/json; charset=utf-8";
        }

        [JsGlobal("Object")]
        public class Peladen
        {
            [JsName("listen")] public void Dengarkan(int port, Action ketikaSiap) => throw new JsExternException();
        }

        [JsImport("node:http", "default")]
        public static class NodeHttp
        {
            [JsName("createServer")]
            public static Peladen BuatPeladen(Action<Permintaan, Respons> penangan) => throw new JsExternException();
        }

        /// <summary>Satu buku dalam koleksi.</summary>
        public record Buku(int Id, string Judul, string Penulis, int Tahun);

        [JsExport(Global = true)]
        public static class Api
        {
            private static readonly List<Buku> Koleksi = new()
            {
                new Buku(1, "Bumi Manusia", "Pramoedya Ananta Toer", 1980),
                new Buku(2, "Laskar Pelangi", "Andrea Hirata", 2005),
                new Buku(3, "Cantik Itu Luka", "Eka Kurniawan", 2002),
            };

            public static void Main()
            {
                var peladen = NodeHttp.BuatPeladen(Tangani);
                peladen.Dengarkan(3000, () => Console.WriteLine("API berjalan di http://localhost:3000"));
            }

            private static void Tangani(Permintaan permintaan, Respons respons)
            {
                var jalur = permintaan.Url;

                if (jalur == "/api/buku")
                {
                    Kirim(respons, 200, Json.Stringify(Koleksi));
                    return;
                }

                if (jalur.StartsWith("/api/buku/"))
                {
                    var potongan = jalur.Split('/');
                    var id = int.Parse(potongan[potongan.Length - 1]);
                    var buku = Koleksi.FirstOrDefault(b => b.Id == id);

                    if (buku is null) Kirim(respons, 404, "{\"galat\":\"Buku tidak ditemukan\"}");
                    else Kirim(respons, 200, Json.Stringify(buku));
                    return;
                }

                if (jalur == "/api/sehat")
                {
                    Kirim(respons, 200, "{\"status\":\"ok\"}");
                    return;
                }

                Kirim(respons, 404, "{\"galat\":\"Rute tidak dikenal\"}");
            }

            private static void Kirim(Respons respons, int kode, string isi)
            {
                respons.TulisKepala(kode, new HeaderJson());
                respons.Selesai(isi);
            }
        }
        """;

    private const string ChatServer = """
        using System;
        using System.Collections.Generic;
        using CSJS;

        // Peladen obrolan realtime di atas WebSocket. Perlu paket `ws`:
        //     npm install ws

        [JsObjectLiteral]
        public class OpsiPeladen
        {
            [JsName("port")] public int Port { get; set; }
        }

        /// <summary>Satu koneksi klien.</summary>
        [JsGlobal("Object")]
        public class Koneksi
        {
            [JsName("send")] public void Kirim(string pesan) => throw new JsExternException();
            [JsName("on")] public void Pada(string peristiwa, Action<string> penangan) => throw new JsExternException();
            [JsName("close")] public void Tutup() => throw new JsExternException();
        }

        [JsImport("ws", "WebSocketServer")]
        public class PeladenWebSocket
        {
            public PeladenWebSocket(OpsiPeladen opsi) => throw new JsExternException();

            [JsName("on")] public void Pada(string peristiwa, Action<Koneksi> penangan) => throw new JsExternException();
        }

        [JsExport(Global = true)]
        public static class PeladenObrolan
        {
            private static readonly List<Koneksi> Klien = new();
            private static int _berikutnya = 1;

            public static void Main()
            {
                var peladen = new PeladenWebSocket(new OpsiPeladen { Port = 8080 });

                peladen.Pada("connection", koneksi =>
                {
                    var nama = $"tamu-{_berikutnya}";
                    _berikutnya++;
                    Klien.Add(koneksi);

                    Console.WriteLine($"{nama} bergabung — {Klien.Count} klien terhubung.");
                    Siarkan($"{nama} bergabung.");

                    koneksi.Pada("message", data => Siarkan($"{nama}: {data}"));

                    koneksi.Pada("close", _ =>
                    {
                        Klien.Remove(koneksi);
                        Console.WriteLine($"{nama} keluar — {Klien.Count} klien tersisa.");
                        Siarkan($"{nama} keluar.");
                    });
                });

                Console.WriteLine("Peladen obrolan berjalan di ws://localhost:8080");
            }

            /// <summary>Mengirim satu pesan ke setiap klien yang masih terhubung.</summary>
            private static void Siarkan(string pesan)
            {
                foreach (var klien in Klien) klien.Kirim(pesan);
            }
        }
        """;

    private const string ChatClient = """
        using System;
        using CSJS;
        using CSJS.Browser;

        // Klien obrolan yang berjalan di browser. WebSocket adalah global browser,
        // jadi tidak ada modul yang perlu diimpor.

        [JsGlobal("WebSocket")]
        public class SoketWeb
        {
            public SoketWeb(string alamat) => throw new JsExternException();

            [JsName("send")] public void Kirim(string pesan) => throw new JsExternException();
            [JsName("addEventListener")] public void Pada(string peristiwa, Action<Event> penangan)
                => throw new JsExternException();
        }

        [JsExport(Global = true)]
        public static class KlienObrolan
        {
            private static SoketWeb? _soket;

            public static void Start()
            {
                _soket = new SoketWeb("ws://localhost:8080");

                _soket.Pada("open", _ => Catat("Terhubung ke peladen."));
                _soket.Pada("close", _ => Catat("Sambungan terputus."));

                var kirim = Document.GetElementById("kirim");
                kirim?.AddEventListener("click", _ => Kirim());

                var masukan = Document.GetElementById("pesan");
                masukan?.AddEventListener("keydown", e =>
                {
                    if (e is KeyboardEvent tombol && tombol.Key == "Enter") Kirim();
                });
            }

            private static void Kirim()
            {
                var masukan = Document.GetElementById("pesan");
                if (masukan is null) return;

                var teks = masukan.Value.Trim();
                if (teks.Length == 0) return;

                _soket?.Kirim(teks);
                masukan.Value = "";
            }

            private static void Catat(string baris)
            {
                var daftar = Document.GetElementById("riwayat");
                if (daftar is null) return;

                var item = Document.CreateElement("li");
                item.TextContent = baris;
                daftar.AppendChild(item);
            }
        }
        """;

    private const string CronWorker = """
        using System;
        using System.Collections.Generic;
        using CSJS;
        using CSJS.Browser;

        // Layanan latar belakang: pekerjaan terjadwal yang berjalan terus di Node.
        // `Timers` memetakan ke setInterval/setTimeout, yang merupakan global di Node
        // maupun di browser — jadi binding yang sama berlaku di kedua tempat.

        /// <summary>Satu pekerjaan terjadwal.</summary>
        public sealed class Pekerjaan
        {
            public Pekerjaan(string nama, double setiapDetik, Action tugas)
            {
                Nama = nama;
                SetiapDetik = setiapDetik;
                Tugas = tugas;
            }

            public string Nama { get; }
            public double SetiapDetik { get; }
            public Action Tugas { get; }
            public int Jalan { get; set; }
            public int Gagal { get; set; }
        }

        /// <summary>
        /// Penjadwal sederhana. Setiap pekerjaan mendapat intervalnya sendiri, dan kegagalan
        /// satu pekerjaan tidak menghentikan yang lain — itulah gunanya try/catch di dalam
        /// pembungkus, bukan di sekeliling seluruh penjadwal.
        /// </summary>
        public static class Penjadwal
        {
            private static readonly List<Pekerjaan> Daftar = new();
            private static readonly List<int> Pegangan = new();

            public static void Tambah(string nama, double setiapDetik, Action tugas)
                => Daftar.Add(new Pekerjaan(nama, setiapDetik, tugas));

            public static void Mulai()
            {
                foreach (var pekerjaan in Daftar)
                {
                    Console.WriteLine($"Menjadwalkan '{pekerjaan.Nama}' setiap {pekerjaan.SetiapDetik} detik.");

                    var pegangan = Timers.SetInterval(() => Jalankan(pekerjaan), pekerjaan.SetiapDetik * 1000);
                    Pegangan.Add(pegangan);
                }
            }

            public static void Berhenti()
            {
                foreach (var pegangan in Pegangan) Timers.ClearInterval(pegangan);
                Pegangan.Clear();
                Console.WriteLine("Penjadwal dihentikan.");
            }

            private static void Jalankan(Pekerjaan pekerjaan)
            {
                try
                {
                    pekerjaan.Tugas();
                    pekerjaan.Jalan++;
                }
                catch (Exception galat)
                {
                    pekerjaan.Gagal++;
                    Console.WriteLine($"[{pekerjaan.Nama}] gagal: {galat.Message}");
                }
            }

            public static void Ringkasan()
            {
                Console.WriteLine("--- ringkasan ---");
                foreach (var pekerjaan in Daftar)
                    Console.WriteLine($"  {pekerjaan.Nama}: {pekerjaan.Jalan} jalan, {pekerjaan.Gagal} gagal");
            }
        }

        [JsExport(Global = true)]
        public static class Pekerja
        {
            public static void Main()
            {
                Console.WriteLine("Layanan latar belakang dimulai. Tekan Ctrl+C untuk berhenti.");

                Penjadwal.Tambah("denyut", 5, () =>
                    Console.WriteLine($"[denyut] {DateTime.Now.ToString()} — layanan hidup."));

                Penjadwal.Tambah("bersih-bersih", 15, () =>
                {
                    var dibuang = Bersihkan();
                    Console.WriteLine($"[bersih-bersih] {dibuang} entri kedaluwarsa dibuang.");
                });

                Penjadwal.Tambah("laporan", 30, Penjadwal.Ringkasan);

                Penjadwal.Mulai();
            }

            /// <summary>Contoh pekerjaan nyata: membuang entri yang sudah lewat waktunya.</summary>
            private static int Bersihkan()
            {
                var sekarang = DateTime.Now;
                var dibuang = 0;

                foreach (var kunci in new List<string>(Singgahan.Keys))
                {
                    if (Singgahan[kunci] < sekarang)
                    {
                        Singgahan.Remove(kunci);
                        dibuang++;
                    }
                }

                return dibuang;
            }

            private static readonly Dictionary<string, DateTime> Singgahan = new();
        }
        """;
}
