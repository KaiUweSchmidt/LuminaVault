var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL with pgvector extension
var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

var metadataDb = postgres.AddDatabase("luminavault-metadata");
var vectorDb = postgres.AddDatabase("luminavault-vectors");

// MinIO object storage
var minio = builder.AddMinioContainer("minio")
    .WithLifetime(ContainerLifetime.Persistent);

// Backend services
var metadataStorage = builder.AddProject<Projects.LuminaVault_MetadataStorage>("metadata-storage")
    .WithReference(metadataDb)
    .WaitFor(metadataDb);

var aiTagging = builder.AddProject<Projects.LuminaVault_AiTagging>("ai-tagging")
    .WithReference(metadataDb)
    .WaitFor(metadataDb);

var vectorSearch = builder.AddProject<Projects.LuminaVault_VectorSearch>("vector-search")
    .WithReference(vectorDb)
    .WaitFor(vectorDb);

var thumbnailGeneration = builder.AddProject<Projects.LuminaVault_ThumbnailGeneration>("thumbnail-generation")
    .WithReference(minio)
    .WaitFor(minio);

var mediaImport = builder.AddProject<Projects.LuminaVault_MediaImport>("media-import")
    .WithReference(minio)
    .WithReference(metadataDb)
    .WithReference(metadataStorage)
    .WithReference(thumbnailGeneration)
    .WaitFor(minio)
    .WaitFor(metadataDb);

// API Gateway
var apiGateway = builder.AddProject<Projects.LuminaVault_ApiGateway>("api-gateway")
    .WithReference(metadataStorage)
    .WithReference(aiTagging)
    .WithReference(vectorSearch)
    .WithReference(thumbnailGeneration)
    .WithReference(mediaImport)
    .WaitFor(metadataStorage)
    .WaitFor(aiTagging)
    .WaitFor(vectorSearch)
    .WaitFor(thumbnailGeneration)
    .WaitFor(mediaImport);

// Web UI
builder.AddProject<Projects.LuminaVault_WebUI>("webui")
    .WithReference(apiGateway)
    .WaitFor(apiGateway);

builder.Build().Run();
