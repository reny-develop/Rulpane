using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Rulpane.Engine;
using Rulpane.Viewer;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

var origin = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = origin });

// wwwroot/appsettings.json may say where to read the Registry, the feed or the Gallery instead,
// relative to the viewer's own address: a copy served with its own Gallery beside it, as the
// Gallery's pictures are taken, reads that one. It ships empty, and is there to be overwritten
// after publishing, since the viewer reads it only if it was there when it was published.
foreach ((string key, Action<Uri> set) in new (string, Action<Uri>)[]
{
    ("Registry", u => Addresses.Registry = u),
    ("Feed", u => Addresses.Feed = u),
    ("Gallery", u => Addresses.Gallery = u),
})
{
    if (builder.Configuration[key] is string address)
    {
        set(new Uri(origin, address));
    }
}

await builder.Build().RunAsync();
