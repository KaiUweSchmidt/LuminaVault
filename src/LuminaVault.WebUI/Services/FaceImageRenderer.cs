using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuminaVault.WebUI.Services;

/// <summary>
/// Draws face bounding boxes directly onto an image and returns the result as a JPEG data URL.
/// </summary>
public static class FaceImageRenderer
{
    private static readonly Color[] FaceColors =
    [
        Color.Lime, Color.Cyan, Color.Magenta, Color.Yellow, Color.OrangeRed,
        Color.DeepSkyBlue, Color.HotPink, Color.Chartreuse, Color.Gold, Color.Aqua
    ];

    /// <summary>
    /// Draws bounding boxes onto the image and returns a base64-encoded JPEG data URL.
    /// </summary>
    public static string Render(byte[] imageBytes, IReadOnlyList<Face> faces)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        DrawBoxes(image, faces);

        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms, new JpegEncoder { Quality = 85 });
        var base64 = Convert.ToBase64String(ms.ToArray());
        return $"data:image/jpeg;base64,{base64}";
    }

    private static void DrawBoxes(Image<Rgba32> image, IReadOnlyList<Face> faces)
    {
        var font = SystemFonts.CreateFont("DejaVu Sans", Math.Max(16, image.Width / 60f), FontStyle.Bold);

        image.Mutate(ctx =>
        {
            for (var i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                if (face.BboxWidth <= 0 || face.BboxHeight <= 0)
                    continue;

                var color = FaceColors[i % FaceColors.Length];
                var pen = Pens.Solid(color, Math.Max(2, image.Width / 500f));

                var x = (float)(face.BboxX / 100.0 * image.Width);
                var y = (float)(face.BboxY / 100.0 * image.Height);
                var w = (float)(face.BboxWidth / 100.0 * image.Width);
                var h = (float)(face.BboxHeight / 100.0 * image.Height);

                ctx.Draw(pen, new RectangleF(x, y, w, h));

                var label = string.IsNullOrWhiteSpace(face.Name) ? $"Gesicht {i + 1}" : face.Name;
                var labelY = Math.Max(0, y - font.Size - 4);
                ctx.DrawText(label, font, color, new PointF(x, labelY));
            }
        });
    }
}
