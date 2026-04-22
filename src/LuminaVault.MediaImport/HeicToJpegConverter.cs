using ImageMagick;

namespace LuminaVault.MediaImport;

/// <summary>
/// Konvertiert HEIC/HEIF-Bilder in das JPEG-Format.
/// </summary>
public static class HeicToJpegConverter
{
    private static readonly HashSet<string> HeicContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/heic",
        "image/heif",
        "image/heic-sequence",
        "image/heif-sequence"
    };

    private static readonly HashSet<string> HeicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".heic",
        ".heif"
    };

    /// <summary>
    /// Prüft anhand von Content-Type oder Dateiendung, ob es sich um ein HEIC/HEIF-Bild handelt.
    /// </summary>
    public static bool IsHeic(string? contentType, string? fileName)
    {
        if (contentType is not null && HeicContentTypes.Contains(contentType))
            return true;

        if (fileName is not null)
        {
            var extension = Path.GetExtension(fileName);
            if (HeicExtensions.Contains(extension))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Konvertiert einen HEIC/HEIF-Stream in JPEG und gibt die JPEG-Bytes zurück.
    /// </summary>
    public static byte[] ConvertToJpeg(Stream heicStream, int quality = 90)
    {
        ArgumentNullException.ThrowIfNull(heicStream);

        using var image = new MagickImage(heicStream);
        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)quality;

        using var output = new MemoryStream();
        image.Write(output);
        return output.ToArray();
    }

    /// <summary>
    /// Konvertiert einen HEIC/HEIF-Stream in JPEG und schreibt das Ergebnis in den Ziel-Stream.
    /// </summary>
    public static async Task ConvertToJpegAsync(Stream heicStream, Stream destination, int quality = 90, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heicStream);
        ArgumentNullException.ThrowIfNull(destination);

        // MagickImage hat keine native async API, daher laden wir den Input-Stream in den Speicher
        using var buffer = new MemoryStream();
        await heicStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var image = new MagickImage(buffer);
        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)quality;

        image.Write(destination);
        destination.Position = 0;
    }

    /// <summary>
    /// Ersetzt die Dateiendung durch .jpg.
    /// </summary>
    public static string ReplaceExtensionWithJpg(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return Path.ChangeExtension(fileName, ".jpg");
    }
}
