using LuminaVault.GeocodingService;
using LuminaVault.ServiceDefaults;
using Minio;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNatsClient();

builder.Services.AddMemoryCache();

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .Build();
});

builder.Services.AddHttpClient<GisgraphyClient>(client =>
{
    var gisgraphyUrl = builder.Configuration["Services:Gisgraphy"] ?? "http://gisgraphy:8080";
    client.BaseAddress = new Uri(gisgraphyUrl);
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHttpClient<GeocodingMetadataClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:MetadataStorage"]
        ?? "http://metadata-storage:8080"));

// JetStream consumer for async geocoding of imported media
builder.Services.AddHostedService<NatsGeocodingSubscriber>();

// Legacy request/reply worker for synchronous geocoding requests
builder.Services.AddHostedService<GeocodingWorker>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[GeocodingService] ===== Service gestartet (JetStream + Request/Reply) =====");

app.Run();
