using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace LuminaVault.MediaImport;

/// <summary>
/// Extracts GPS coordinates from EXIF metadata embedded in image files.
/// </summary>
public static class GpsExifExtractor
{
    /// <summary>
    /// Reads the GPS latitude and longitude from the EXIF data of an image stream.
    /// Returns null if no GPS data is present or the stream is not a supported image.
    /// </summary>
    public static (double Latitude, double Longitude)? ExtractGps(Stream imageStream)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imageStream);
            var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
            if (gps is null) return null;

            var location = gps.GetGeoLocation();
            if (location is null) return null;

            return (location.Latitude, location.Longitude);
        }
        catch
        {
            return null;
        }
    }
}
