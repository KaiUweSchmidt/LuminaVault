using LuminaVault.WebUI.Components;
using LuminaVault.WebUI.Services;
using LuminaVault.WebUI.Settings;
using Microsoft.AspNetCore.Components.Server.Circuits;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNatsClient();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ImportSettings>(builder.Configuration.GetSection(ImportSettings.SectionName));

builder.Services.AddScoped<GalleryUserSettingsService>();

// Pipeline completion tracking via NATS
builder.Services.AddSingleton<PipelineCompletionTracker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PipelineCompletionTracker>());

// Batch import: singleton state + scoped circuit handler for pause-on-disconnect
builder.Services.AddSingleton<BatchImportService>();
builder.Services.AddSingleton<HostMediaPathService>();
builder.Services.AddScoped<BatchImportCircuitHandler>();
builder.Services.AddScoped<CircuitHandler>(sp => sp.GetRequiredService<BatchImportCircuitHandler>());

builder.Services.AddHttpClient<MediaApiClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:ApiGateway"]
        ?? "http://api-gateway:8080"));

var app = builder.Build();

app.Logger.LogInformation("LuminaVault WebUI starting in {Environment} environment", app.Environment.EnvironmentName);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Proxy endpoints — serve media files through WebUI so internal services stay unexposed
app.MapGet("/proxy/thumbnail/{mediaId:guid}", async (Guid mediaId, MediaApiClient mediaApi) =>
{
    var bytes = await mediaApi.GetThumbnailBytesAsync(mediaId);
    return bytes is not null
        ? Results.File(bytes, "image/jpeg")
        : Results.NotFound();
});

app.MapGet("/proxy/media/{mediaId:guid}", async (Guid mediaId, MediaApiClient mediaApi) =>
{
    var result = await mediaApi.GetOriginalBytesAsync(mediaId);
    return result.HasValue
        ? Results.File(result.Value.Data, result.Value.ContentType)
        : Results.NotFound();
});

app.MapDefaultEndpoints();

app.Logger.LogInformation("LuminaVault WebUI configured and ready to start");

app.Run();
