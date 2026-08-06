// ASP.NET Core host. The browser-side code lives in CsjsClient/ and is transpiled to
// wwwroot/js by the TranspileCsjs target in the .csproj, so `dotnet run` is all you need.
//
// Gravicode Studios — led by Kang Fadhil.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// The client projects this JSON into its own typed C# Product class, so both ends of the
// request describe the same shape.
app.MapGet("/api/products", () => Results.Json(new
{
    items = new[]
    {
        new { id = 1, name = "Kopi Gayo",       price = 85000.0,  category = "kopi" },
        new { id = 2, name = "Kopi Toraja",     price = 92000.0,  category = "kopi" },
        new { id = 3, name = "Teh Melati",      price = 35000.0,  category = "teh" },
        new { id = 4, name = "Teh Oolong",      price = 68000.0,  category = "teh" },
        new { id = 5, name = "Gula Aren",       price = 24000.0,  category = "pemanis" },
        new { id = 6, name = "Madu Hutan",      price = 130000.0, category = "pemanis" },
    },
}));

app.Run();
