namespace CSJS.Playground;

/// <summary>A ready-made snippet offered in the playground's sample picker.</summary>
/// <param name="Name">Label shown in the dropdown.</param>
/// <param name="Code">The C# source loaded into the editor.</param>
public sealed record Sample(string Name, string Code);

/// <summary>
/// The snippets the playground ships with.
/// <para>
/// Each one is chosen to show a translation decision worth seeing: integer division truncating,
/// a Dictionary becoming a Map, an iterator becoming a generator. Someone reading the emitted
/// JavaScript beside the C# should come away understanding what the transpiler actually does.
/// </para>
/// </summary>
public static class Samples
{
    public static IReadOnlyList<Sample> All { get; } = new Sample[]
    {
        new("Halo Dunia", """
            using System;
            using CSJS;
            using CSJS.Browser;

            /// <summary>Dipanggil dari HTML setelah modul dimuat.</summary>
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
            """),

        new("Koleksi dan LINQ", """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            public class Produk
            {
                public string Nama { get; set; } = "";
                public double Harga { get; set; }
                public string Kategori { get; set; } = "";
            }

            public static class Toko
            {
                public static void Ringkas()
                {
                    var produk = new List<Produk>
                    {
                        new Produk { Nama = "Kopi Gayo", Harga = 85000, Kategori = "kopi" },
                        new Produk { Nama = "Teh Melati", Harga = 35000, Kategori = "teh" },
                        new Produk { Nama = "Kopi Toraja", Harga = 92000, Kategori = "kopi" },
                    };

                    // List<T> menjadi Array, jadi filter dan map langsung dipakai.
                    var murah = produk.Where(p => p.Harga < 90000)
                                      .OrderBy(p => p.Harga)
                                      .Select(p => p.Nama)
                                      .ToList();

                    Console.WriteLine(string.Join(", ", murah));
                    Console.WriteLine($"Total: {produk.Sum(p => p.Harga)}");

                    foreach (var grup in produk.GroupBy(p => p.Kategori))
                    {
                        Console.WriteLine($"{grup.Key}: {grup.Count()} item");
                    }
                }
            }
            """),

        new("Dictionary dan Set", """
            using System;
            using System.Collections.Generic;

            public static class Contoh
            {
                public static void Jalankan()
                {
                    // Dictionary menjadi Map, bukan objek biasa.
                    var nilai = new Dictionary<string, int>
                    {
                        ["ana"] = 90,
                        ["budi"] = 72,
                    };

                    nilai["citra"] = 85;

                    if (nilai.TryGetValue("ana", out var skorAna))
                    {
                        Console.WriteLine($"Ana: {skorAna}");
                    }

                    foreach (var entri in nilai)
                    {
                        Console.WriteLine($"{entri.Key} = {entri.Value}");
                    }

                    // HashSet menjadi Set.
                    var unik = new HashSet<int> { 1, 2, 2, 3 };
                    Console.WriteLine($"Anggota unik: {unik.Count}");
                }
            }
            """),

        new("Pembagian bilangan bulat", """
            using System;

            public static class Aritmetika
            {
                public static void Jalankan()
                {
                    int a = 7, b = 2;

                    // C# memotong hasil bagi bilangan bulat; JavaScript tidak.
                    // Perhatikan Math.trunc pada keluaran.
                    Console.WriteLine(a / b);
                    Console.WriteLine(-7 / 2);
                    Console.WriteLine(7.0 / 2);

                    var x = 10;
                    x /= 3;
                    Console.WriteLine(x);
                }
            }
            """),

        new("Record dan pattern matching", """
            using System;

            public record Titik(int X, int Y);

            public enum Tingkat { Rendah = 1, Sedang = 2, Tinggi = 3 }

            public static class Contoh
            {
                public static void Jalankan()
                {
                    var a = new Titik(1, 2);
                    var b = new Titik(1, 2);

                    // Record dibandingkan berdasarkan nilai, bukan referensi.
                    Console.WriteLine(a == b);
                    Console.WriteLine(a.ToString());

                    Console.WriteLine(Klasifikasi(85));
                    Console.WriteLine(Deskripsi(a));
                }

                private static Tingkat Klasifikasi(int skor) => skor switch
                {
                    >= 80 => Tingkat.Tinggi,
                    >= 50 => Tingkat.Sedang,
                    _ => Tingkat.Rendah,
                };

                private static string Deskripsi(object nilai) => nilai switch
                {
                    Titik { X: 0, Y: 0 } => "titik asal",
                    Titik t => $"titik ({t.X}, {t.Y})",
                    int n when n < 0 => "bilangan negatif",
                    string s => $"teks sepanjang {s.Length}",
                    _ => "lainnya",
                };
            }
            """),

        new("Iterator dan async", """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public static class Contoh
            {
                // Iterator C# menjadi generator JavaScript.
                public static IEnumerable<int> Fibonacci(int jumlah)
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

                // Task menjadi Promise; async/await terbawa apa adanya.
                public static async Task<int> HitungTertunda(int nilai)
                {
                    await Task.Delay(100);
                    return nilai * 2;
                }

                public static async Task Jalankan()
                {
                    foreach (var n in Fibonacci(8))
                    {
                        Console.Write($"{n} ");
                    }
                    Console.WriteLine();

                    var hasil = await HitungTertunda(21);
                    Console.WriteLine(hasil);
                }
            }
            """),

        new("DOM dan event", """
            using System;
            using System.Collections.Generic;
            using CSJS;
            using CSJS.Browser;

            [JsExport(Global = true)]
            public static class Penghitung
            {
                private static int _jumlah;

                public static void Start()
                {
                    var tombol = Document.GetElementById("tambah");
                    tombol?.AddEventListener("click", _ =>
                    {
                        _jumlah++;
                        Perbarui();
                    });

                    var reset = Document.GetElementById("reset");
                    reset?.AddEventListener("click", _ =>
                    {
                        _jumlah = 0;
                        Perbarui();
                    });

                    Perbarui();
                }

                private static void Perbarui()
                {
                    var layar = Document.GetElementById("layar");
                    if (layar is null) return;

                    layar.TextContent = _jumlah.ToString();
                    layar.ClassList.Toggle("besar", _jumlah >= 10);
                }
            }
            """),

        new("Interop pustaka JS", """
            using CSJS;

            // Binding ke pustaka JavaScript pihak ketiga. Tidak ada kelas yang dihasilkan —
            // transpiler memancarkan import ES sungguhan.

            [JsImport("three", "Scene")]
            public class Scene
            {
                public Scene() => throw new JsExternException();
                public void Add(object objek) => throw new JsExternException();
            }

            [JsImport("three", "BoxGeometry")]
            public class BoxGeometry
            {
                public BoxGeometry(double w, double h, double d) => throw new JsExternException();
            }

            [JsObjectLiteral]
            public class OpsiMaterial
            {
                [JsName("color")] public int Warna { get; set; }
                [JsName("wireframe")] public bool Rangka { get; set; }
            }

            [JsImport("three", "MeshBasicMaterial")]
            public class MeshBasicMaterial
            {
                public MeshBasicMaterial(OpsiMaterial opsi) => throw new JsExternException();
            }

            [JsImport("three", "Mesh")]
            public class Mesh
            {
                public Mesh(BoxGeometry geometri, MeshBasicMaterial material)
                    => throw new JsExternException();
            }

            [JsExport(Global = true)]
            public static class App
            {
                public static void Bangun()
                {
                    var scene = new Scene();
                    var kubus = new Mesh(
                        new BoxGeometry(1, 1, 1),
                        new MeshBasicMaterial(new OpsiMaterial { Warna = 0xfb923c, Rangka = true }));

                    scene.Add(kubus);
                }
            }
            """),

        new("Yang tidak didukung", """
            using System;

            // Tidak semua C# bisa ditranspilasi. Transpiler menolaknya secara eksplisit
            // daripada memancarkan JavaScript yang gagal saat dijalankan.

            public static class Contoh
            {
                // ref dan out tidak punya padanan di JavaScript.
                private static void Tukar(ref int a, ref int b)
                {
                    (a, b) = (b, a);
                }

                public static void Jalankan()
                {
                    int x = 1, y = 2;
                    Tukar(ref x, ref y);
                    Console.WriteLine($"{x} {y}");
                }

                // Gunakan tuple sebagai gantinya:
                private static (int, int) TukarBenar(int a, int b) => (b, a);
            }
            """),
    };
}
