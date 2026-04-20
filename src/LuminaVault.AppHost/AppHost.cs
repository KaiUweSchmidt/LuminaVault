var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector extension
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg17");

var metadataDb = postgres.AddDatabase("luminavault-metadata");
var vectorDb = postgres.AddDatabase("luminavault-vectors");

// MinIO object storage
var minio = builder.AddMinioContainer("minio")
    .WithLifetime(ContainerLifetime.Persistent);

// Ollama for AI/LLM inference
var ollama = builder.AddOllama("ollama")
    .WithLifetime(ContainerLifetime.Persistent)
    .AddModel("llava:13b");

// Backend services
var metadataStorage = builder.AddProject<Projects.LuminaVault_MetadataStorage>("metadata-storage")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(metadataDb)
    .WaitFor(metadataDb);

var aiTagging = builder.AddProject<Projects.LuminaVault_AiTagging>("ai-tagging")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(metadataDb)
    .WaitFor(metadataDb);

var vectorSearch = builder.AddProject<Projects.LuminaVault_VectorSearch>("vector-search")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(vectorDb)
    .WaitFor(vectorDb);

var thumbnailGeneration = builder.AddProject<Projects.LuminaVault_ThumbnailGeneration>("thumbnail-generation")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(minio)
    .WaitFor(minio);

var objectRecognition = builder.AddProject<Projects.LuminaVault_ObjectRecognition>("object-recognition")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(minio)
    .WithReference(metadataStorage)
    .WithReference(ollama)
    .WaitFor(minio)
    .WaitFor(metadataStorage)
    .WaitFor(ollama);

var mediaImport = builder.AddProject<Projects.LuminaVault_MediaImport>("media-import")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(minio)
    .WithReference(metadataDb)
    .WithReference(metadataStorage)
    .WithReference(thumbnailGeneration)
    .WithReference(objectRecognition)
    .WaitFor(minio)
    .WaitFor(metadataDb);

// API Gateway
var apiGateway = builder.AddProject<Projects.LuminaVault_ApiGateway>("api-gateway")
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(metadataStorage)
    .WithReference(aiTagging)
    .WithReference(vectorSearch)
    .WithReference(thumbnailGeneration)
    .WithReference(mediaImport)
    .WithReference(objectRecognition)
    .WaitFor(metadataStorage)
    .WaitFor(aiTagging)
    .WaitFor(vectorSearch)
    .WaitFor(thumbnailGeneration)
    .WaitFor(mediaImport);

// Web UI
builder.AddProject<Projects.LuminaVault_WebUI>("webui")
    .WithExternalHttpEndpoints()
    .WithEnvironment("DOTNET_STARTUP_HOOKS", "")
    .WithReference(apiGateway)
    .WaitFor(apiGateway);

builder.Build().Run();
