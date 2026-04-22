using Microsoft.EntityFrameworkCore;

namespace LuminaVault.GeocodingService;

/// <summary>
/// EF Core DbContext for caching resolved geocoding results in PostgreSQL.
/// </summary>
public class GeocodingDbContext(DbContextOptions<GeocodingDbContext> options) : DbContext(options)
{
    public DbSet<GeocodingCacheEntry> GeocodingCache => Set<GeocodingCacheEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GeocodingCacheEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LatitudeRounded, x.LongitudeRounded }).IsUnique();
        });
    }
}

/// <summary>
/// A cached reverse-geocoding result keyed on rounded coordinates.
/// </summary>
public sealed class GeocodingCacheEntry
{
    public int Id { get; set; }

    /// <summary>Latitude rounded to 3 decimal places (~111 m precision).</summary>
    public double LatitudeRounded { get; set; }

    /// <summary>Longitude rounded to 3 decimal places (~111 m precision).</summary>
    public double LongitudeRounded { get; set; }

    /// <summary>Resolved location name, or null when geocoding returned no result.</summary>
    public string? LocationName { get; set; }

    public DateTimeOffset CachedAt { get; set; }
}
