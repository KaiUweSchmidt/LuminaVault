using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace LuminaVault.MediaImport;

/// <summary>
/// Extracts the capture date from EXIF metadata embedded in image files.
/// </summary>
public static class ExifDateExtractor
{
    /// <summary>
    /// Reads the DateTimeOriginal from the EXIF data of an image stream.
    /// Returns null if no date is present or the stream is not a supported image.
    /// </summary>
    public static DateTimeOffset? ExtractDateTaken(Stream imageStream)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imageStream);
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is null) return null;

            if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTime))
                return new DateTimeOffset(dateTime, TimeSpan.Zero);

            if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out dateTime))
                return new DateTimeOffset(dateTime, TimeSpan.Zero);

            return null;
        }
        catch
        {
            return null;
        }
    }
}
