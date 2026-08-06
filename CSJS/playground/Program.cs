using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CSJS.Playground;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// The transpiler needs reference assemblies, which in WebAssembly must be downloaded rather
// than read from disk. One loader is shared so the download happens once per session.
builder.Services.AddScoped<ReferenceLoader>();

await builder.Build().RunAsync();
