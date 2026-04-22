using LuminaVault.FaceRecognition;
using Minio;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNatsClient();

builder.Services.AddSingleton<IMinioClient>(sp =>
{
    var config = builder.Configuration.GetSection("Minio");
    return new MinioClient()
        .WithEndpoint(config["Endpoint"])
        .WithCredentials(config["AccessKey"], config["SecretKey"])
        .Build();
});

builder.Services.AddHttpClient<OllamaClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var ollamaUrl = config["Services:Ollama"] ?? "http://ollama:11434";
    client.BaseAddress = new Uri(ollamaUrl);
});

builder.Services.AddHttpClient<MetadataStorageClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Services:MetadataStorage"]
        ?? "http://metadata-storage:8080"));

builder.Services.AddSingleton<YoloFaceDetector>(sp =>
{
    var modelPath = builder.Configuration["Yolo:ModelPath"] ?? "/app/models/yolov12m-face.onnx";
    var logger = sp.GetRequiredService<ILogger<YoloFaceDetector>>();
    return new YoloFaceDetector(modelPath, logger);
});

builder.Services.AddHostedService<NatsFaceRecognitionSubscriber>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Logger.LogInformation("[PIPELINE:FaceRec] ===== FaceRecognition Service gestartet — YOLO-Face + Ollama (JetStream) =====");

app.Run();
