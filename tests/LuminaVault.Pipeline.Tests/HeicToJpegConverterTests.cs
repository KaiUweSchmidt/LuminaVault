using ImageMagick;
using LuminaVault.MediaImport;
using Xunit;

namespace LuminaVault.Pipeline.Tests;

public class HeicToJpegConverterTests
{
    [Theory]
    [InlineData("image/heic", "photo.heic", true)]
    [InlineData("image/heif", "photo.heif", true)]
    [InlineData("image/heic-sequence", "burst.heic", true)]
    [InlineData("image/heif-sequence", "burst.heif", true)]
    [InlineData("image/jpeg", "photo.jpg", false)]
    [InlineData("image/png", "photo.png", false)]
    [InlineData(null, "photo.heic", true)]
    [InlineData(null, "photo.jpg", false)]
    [InlineData("image/heic", null, true)]
    [InlineData("application/octet-stream", "photo.HEIC", true)]
    public void WhenCheckingIsHeicThenReturnsExpectedResult(string? contentType, string? fileName, bool expected)
    {
        var result = HeicToJpegConverter.IsHeic(contentType, fileName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void WhenConvertingImageStreamThenReturnsValidJpegBytes()
    {
        using var inputStream = CreateTestImageStream();

        var jpegBytes = HeicToJpegConverter.ConvertToJpeg(inputStream);

        Assert.NotEmpty(jpegBytes);
        // JPEG-Magic-Bytes: FF D8 FF
        Assert.Equal(0xFF, jpegBytes[0]);
        Assert.Equal(0xD8, jpegBytes[1]);
        Assert.Equal(0xFF, jpegBytes[2]);
    }

    [Fact]
    public async Task WhenConvertingImageStreamAsyncThenWritesValidJpegToDestination()
    {
        using var inputStream = CreateTestImageStream();
        using var destination = new MemoryStream();

        await HeicToJpegConverter.ConvertToJpegAsync(inputStream, destination);

        Assert.True(destination.Length > 0);
        destination.Position = 0;
        var header = new byte[3];
        await destination.ReadExactlyAsync(header);
        Assert.Equal(0xFF, header[0]);
        Assert.Equal(0xD8, header[1]);
        Assert.Equal(0xFF, header[2]);
    }

    [Fact]
    public void WhenConvertingNullStreamThenThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HeicToJpegConverter.ConvertToJpeg(null!));
    }

    [Fact]
    public async Task WhenConvertingAsyncWithNullStreamThenThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => HeicToJpegConverter.ConvertToJpegAsync(null!, new MemoryStream()));
    }

    [Fact]
    public async Task WhenConvertingAsyncWithNullDestinationThenThrowsArgumentNullException()
    {
        using var input = CreateTestImageStream();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => HeicToJpegConverter.ConvertToJpegAsync(input, null!));
    }

    [Theory]
    [InlineData("photo.heic", "photo.jpg")]
    [InlineData("IMG_001.HEIC", "IMG_001.jpg")]
    [InlineData("file.heif", "file.jpg")]
    [InlineData("noext", "noext.jpg")]
    public void WhenReplacingExtensionThenReturnsJpgExtension(string input, string expected)
    {
        var result = HeicToJpegConverter.ReplaceExtensionWithJpg(input);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void WhenReplacingExtensionWithNullThenThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HeicToJpegConverter.ReplaceExtensionWithJpg(null!));
    }

    /// <summary>
    /// Erzeugt einen PNG-Stream mit einem 10x10-Pixel-Testbild via Magick.NET.
    /// HEIC-Encoding ist nicht auf allen Plattformen verfügbar, daher nutzen wir PNG als Input.
    /// Die Konvertierungslogik (beliebiges Bildformat → JPEG) ist identisch.
    /// </summary>
    private static MemoryStream CreateTestImageStream()
    {
        using var image = new MagickImage(MagickColors.Red, 10, 10);
        image.Format = MagickFormat.Png;
        var stream = new MemoryStream();
        image.Write(stream);
        stream.Position = 0;
        return stream;
    }
}
