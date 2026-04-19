using LuminaVault.WebUI.Components;
using LuminaVault.WebUI.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<MediaApiClient>(client =>
    client.BaseAddress = new Uri("http://api-gateway"));

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

app.MapDefaultEndpoints();

app.Logger.LogInformation("LuminaVault WebUI configured and ready to start");

app.Run();
