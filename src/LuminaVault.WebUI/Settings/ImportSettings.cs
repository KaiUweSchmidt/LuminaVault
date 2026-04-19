namespace LuminaVault.WebUI.Settings;

/// <summary>
/// Configuration for media import upload limits.
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
}
