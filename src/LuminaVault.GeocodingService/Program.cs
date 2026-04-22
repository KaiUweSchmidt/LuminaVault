using LuminaVault.GeocodingService;
using LuminaVault.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNatsClient();

builder.Services.AddDbContext<GeocodingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("luminavault-metadata")));

builder.Services.AddHttpClient<GisgraphyClient>(client =>
{
    var gisgraphyUrl = builder.Configuration["Services:Gisgraphy"] ?? "http://gisgraphy:8080";
    client.BaseAddress = new Uri(gisgraphyUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHostedService<GeocodingWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[GeocodingService] ===== Service gestartet =====");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GeocodingDbContext>();
    db.Database.Migrate();
}

app.Run();
