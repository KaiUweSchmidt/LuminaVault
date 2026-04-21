using System.Data.Common;
using LuminaVault.MediaImport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using NSubstitute;

namespace LuminaVault.Pipeline.Tests;

internal sealed class ImportApiFactory : WebApplicationFactory<Program>
{
    private readonly DbConnection _connection;

    public IMinioClient MinioClient { get; } = Substitute.For<IMinioClient>();

    public ImportApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            // Remove ALL EF Core and DbContext related services
            var toRemove = services.Where(d =>
                d.ServiceType.FullName?.Contains("EntityFramework") == true
                || d.ServiceType.FullName?.Contains("DbContext") == true
                || d.ServiceType.FullName?.Contains("Npgsql") == true
                || d.ImplementationType?.FullName?.Contains("EntityFramework") == true
                || d.ImplementationType?.FullName?.Contains("DbContext") == true
                || d.ImplementationType?.FullName?.Contains("Npgsql") == true
            ).ToList();

            foreach (var d in toRemove)
                services.Remove(d);

            services.AddDbContext<MediaImportDbContext>(options =>
                options.UseSqlite(_connection)
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

            // Replace MinIO client
            var minioDescriptors = services.Where(
                d => d.ServiceType == typeof(IMinioClient)).ToList();
            foreach (var d in minioDescriptors)
                services.Remove(d);

            services.AddSingleton(MinioClient);
            services.AddKeyedSingleton<IMinioClient>("public", (_, _) => MinioClient);

            // Replace geocoding service with a no-op stub so tests don't make real HTTP calls
            var geocodingDescriptors = services.Where(
                d => d.ServiceType == typeof(IGeocodingService)).ToList();
            foreach (var d in geocodingDescriptors)
                services.Remove(d);

            var geocodingStub = Substitute.For<IGeocodingService>();
            geocodingStub.GetLocationNameAsync(Arg.Any<double>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
                .Returns((string?)null);
            services.AddSingleton(geocodingStub);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}

