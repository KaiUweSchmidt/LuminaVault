namespace LuminaVault.WebUI.Services;

/// <summary>
/// Holds per-session gallery settings that the user can adjust in the Settings page.
/// </summary>
public sealed class GalleryUserSettingsService
{
    /// <summary>
    /// When <see langword="true"/>, face bounding boxes are automatically rendered in the preview
    /// whenever the selection in the media grid changes.
    /// Default is <see langword="false"/>.
    /// </summary>
    public bool AlwaysShowFaceBboxes { get; set; } = false;
}
