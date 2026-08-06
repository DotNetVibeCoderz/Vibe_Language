// Blazor Server host. Page structure and state live in Razor components; browser-side
// rendering work lives in CsjsClient/ and is transpiled to wwwroot/js during build.
//
// Gravicode Studios — led by Kang Fadhil.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<CsjsBlazorApp.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
