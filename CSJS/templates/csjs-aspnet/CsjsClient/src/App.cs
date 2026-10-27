using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CSJS;
using CSJS.Browser;

namespace CsjsAspNetApp.Client;

/// <summary>
/// Shape of the JSON the server's /api/products endpoint returns.
/// <para>
/// The same record could be shared with the server project, which is the practical payoff of
/// writing the browser code in C#: one type definition for both ends of the request.
/// </para>
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public string Category { get; set; } = "";
}

/// <summary>Browser-side application, transpiled to wwwroot/js/App.js by the project build.</summary>
[JsExport(Global = true)]
public static class App
{
    private static readonly List<Product> Products = new();
    private static string _filter = "all";

    public static async Task Start()
    {
        await LoadProducts();
        WireFilters();
        Render();
    }

    private static async Task LoadProducts()
    {
        var status = Document.GetElementById("status");
        if (status is not null) status.TextContent = "Memuat produk…";

        var response = await Http.Fetch("/api/products");
        if (!response.Ok)
        {
            if (status is not null) status.TextContent = $"Gagal memuat ({response.Status}).";
            return;
        }

        var payload = await response.Json();
        Products.Clear();

        // The response is untyped JSON, so each entry is read through JsObject and
        // projected into the typed C# shape above.
        var items = payload.Get<JsObject[]>("items");
        foreach (var item in items)
        {
            Products.Add(new Product
            {
                Id = item.Get<int>("id"),
                Name = item.Get<string>("name"),
                Price = item.Get<double>("price"),
                Category = item.Get<string>("category"),
            });
        }

        if (status is not null) status.TextContent = $"{Products.Count} produk dimuat.";
    }

    private static void WireFilters()
    {
        foreach (var button in Document.QuerySelectorAll("[data-filter]"))
        {
            var value = button.GetAttribute("data-filter") ?? "all";
            button.AddEventListener("click", _ =>
            {
                _filter = value;
                UpdateActiveFilter();
                Render();
            });
        }

        UpdateActiveFilter();
    }

    private static void UpdateActiveFilter()
    {
        foreach (var button in Document.QuerySelectorAll("[data-filter]"))
        {
            var value = button.GetAttribute("data-filter") ?? "all";
            button.ClassList.Toggle("active", value == _filter);
        }
    }

    private static void Render()
    {
        var grid = Document.GetElementById("grid");
        if (grid is null) return;

        grid.InnerHtml = "";

        var visible = _filter == "all"
            ? Products
            : Products.Where(p => p.Category == _filter).ToList();

        foreach (var product in visible.OrderBy(p => p.Price))
        {
            grid.AppendChild(BuildCard(product));
        }

        var total = Document.GetElementById("total");
        if (total is not null)
        {
            var sum = visible.Sum(p => p.Price);
            total.TextContent = $"{visible.Count} item — total Rp {sum}";
        }
    }

    private static Element BuildCard(Product product)
    {
        var card = Document.CreateElement("article");
        card.ClassName = "card";

        var name = Document.CreateElement("h3");
        name.TextContent = product.Name;

        var category = Document.CreateElement("span");
        category.ClassName = "badge";
        category.TextContent = product.Category;

        var price = Document.CreateElement("p");
        price.ClassName = "price";
        price.TextContent = $"Rp {product.Price}";

        card.AppendChild(category);
        card.AppendChild(name);
        card.AppendChild(price);
        return card;
    }
}
