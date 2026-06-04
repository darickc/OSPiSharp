using MudBlazor.Services;
using OSPi.Application.Persistence;
using OSPi.Infrastructure;
using OSPi.Infrastructure.Persistence;
using OSPi.Mcp;
using OSPi.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

// Sprinkler hardware driver, state hub, scheduling engine, and application services.
builder.Services.AddSprinklerCore(builder.Configuration);

// In-process MCP server (streamable HTTP at /mcp) for AI control. SprinklerTools are stateless
// wrappers over the same application services the UI uses; tool methods receive the singleton
// IManualRunService/IStateHub and scoped repositories via per-request DI.
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly(typeof(SprinklerTools).Assembly);

var app = builder.Build();

// Create/upgrade the SQLite database (applies migrations and seed data on first run).
await app.Services.MigrateDatabaseAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Serves the uploaded property-map image from writable app-data (it lives outside wwwroot
// because the Pi's published binary dir may be read-only). Callers cache-bust with ?v={hash}.
app.MapGet("/property-map/image", async (
    HttpContext http, IPropertyMapRepository maps, ImageStorageOptions storage, CancellationToken ct) =>
{
    var map = await maps.GetAsync(ct);
    if (string.IsNullOrEmpty(map.ImagePath))
    {
        return Results.NotFound();
    }

    var fullPath = Path.Combine(storage.ResolveDirectory(), map.ImagePath);
    if (!File.Exists(fullPath))
    {
        return Results.NotFound();
    }

    // The file name is content-hashed and the URL is versioned, so it's safe to cache forever.
    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    var contentType = map.ImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? "image/png"
        : "image/jpeg";
    return Results.File(fullPath, contentType);
});

// MCP endpoint (streamable HTTP/SSE). It's a JSON/SSE API, not a form post, so it does not
// opt into Blazor's antiforgery. LAN-only, no auth — consistent with the rest of the app.
app.MapMcp("/mcp");

app.Run();
