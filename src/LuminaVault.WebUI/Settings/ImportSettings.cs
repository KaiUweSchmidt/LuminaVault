namespace LuminaVault.WebUI.Settings;

/// <summary>
/// Configuration for media import upload limits and host-path mount settings.
/// </summary>
public sealed class ImportSettings
{
    public const string SectionName = "Import";

    /// <summary>
    /// Maximum number of files that can be selected in a single upload operation.
    /// </summary>
    public int MaxFileCount { get; set; } = 20;

    /// <summary>
    /// Maximum file size in bytes per individual file.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 500 * 1024 * 1024; // 500 MB

    /// <summary>
    /// The container-internal path that is exposed via a Docker bind-mount to a host directory.
    /// Used as the default starting path in the batch import page.
    /// Configure the corresponding bind-mount in docker-compose.yml (or via the env var
    /// <c>Import__HostMediaPath</c>) to point this path at a directory on the Docker host.
    /// </summary>
    public string HostMediaPath { get; set; } = "/media/import";
}
