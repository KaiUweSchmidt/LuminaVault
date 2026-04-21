namespace LuminaVault.ServiceDefaults;

/// <summary>
/// Well-known pipeline step names used in <see cref="PipelineStepCompletedEvent"/>.
/// </summary>
public static class PipelineSteps
{
    public const string MinioUpload = "MinioUpload";
    public const string DatabaseSave = "DatabaseSave";
    public const string ExifExtraction = "ExifExtraction";
    public const string MetadataStorage = "MetadataStorage";
    public const string ThumbnailGeneration = "ThumbnailGeneration";
    public const string ObjectRecognition = "ObjectRecognition";

    /// <summary>Sentinel step — published last to signal the entire pipeline is done.</summary>
    public const string PipelineComplete = "PipelineComplete";
}
