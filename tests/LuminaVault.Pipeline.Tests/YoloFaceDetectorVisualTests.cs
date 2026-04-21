extern alias ObjRec;

using ObjRec::LuminaVault.ObjectRecognition;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace LuminaVault.Pipeline.Tests;

/// <summary>
/// Visual debugging test: runs YOLO face detection and draws bounding boxes onto a copy of the image.
/// Requires the ONNX model and a test image on disk — marked as Slow/Manual.
/// </summary>
public class YoloFaceDetectorVisualTests
{
    private const string ModelPath = @"C:\Git\LuminaVault\models\yolov12m-face.onnx";
    private const string TestDataDir = @"C:\Git\LuminaVault\tests\testdata";
    private const string OutputDir = @"C:\Git\LuminaVault\tests\testdata\output";

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    /// <summary>
    /// Provides all image files in the testdata directory as test cases.
    /// </summary>
    public static TheoryData<string> TestImages()
    {
        var data = new TheoryData<string>();
        if (!Directory.Exists(TestDataDir))
            return data;

        foreach (var file in Directory.EnumerateFiles(TestDataDir)
                     .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
        {
            data.Add(file);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(TestImages))]
    [Trait("Category", "Slow")]
    public void WhenImageProcessedThenBboxIsDrawnOnCopy(string imagePath)
    {
        if (!File.Exists(ModelPath))
            Assert.Skip($"ONNX model not found at {ModelPath}");

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<YoloFaceDetector>();

        using var detector = new YoloFaceDetector(ModelPath, logger);
        var imageBytes = File.ReadAllBytes(imagePath);
        var faces = detector.DetectFaces(imageBytes);

        var fileName = Path.GetFileNameWithoutExtension(imagePath);
        Directory.CreateDirectory(OutputDir);

        using var image = Image.Load<Rgba32>(imageBytes);

        logger.LogInformation("[{File}] Image: {W}x{H}, Detected {Count} face(s)", fileName, image.Width, image.Height, faces.Count);
        foreach (var face in faces)
        {
            logger.LogInformation(
                "[{File}]   Bbox: x={X:F1}%, y={Y:F1}%, w={W:F1}%, h={H:F1}%, conf={Conf:F3}",
                fileName, face.BboxX, face.BboxY, face.BboxWidth, face.BboxHeight, face.Confidence);
        }

        DrawBoxes(image, faces, image.Width, image.Height);
        image.SaveAsJpeg(Path.Combine(OutputDir, $"yolo_bbox_{fileName}.jpg"));

        logger.LogInformation("[{File}] Output saved", fileName);
    }

    private static readonly Color[] FaceColors =
    [
        Color.Lime, Color.Cyan, Color.Magenta, Color.Yellow, Color.OrangeRed,
        Color.DeepSkyBlue, Color.HotPink, Color.Chartreuse, Color.Gold, Color.Aqua
    ];

    private static void DrawBoxes(Image<Rgba32> image, IReadOnlyList<DetectedFace> faces, int imgW, int imgH)
    {
        var font = SystemFonts.CreateFont("DejaVu Sans", 20, FontStyle.Bold);

        image.Mutate(ctx =>
        {
            for (var i = 0; i < faces.Count; i++)
            {
                var color = FaceColors[i % FaceColors.Length];
                var pen = Pens.Solid(color, 3f);

                var face = faces[i];
                var x = (float)(face.BboxX / 100.0 * imgW);
                var y = (float)(face.BboxY / 100.0 * imgH);
                var w = (float)(face.BboxWidth / 100.0 * imgW);
                var h = (float)(face.BboxHeight / 100.0 * imgH);

                ctx.Draw(pen, new RectangleF(x, y, w, h));

                var cx = x + w / 2f;
                var cy = y + h / 2f;
                ctx.DrawLine(pen, new PointF(cx - 10, cy), new PointF(cx + 10, cy));
                ctx.DrawLine(pen, new PointF(cx, cy - 10), new PointF(cx, cy + 10));

                var label = $"Gesicht {i + 1}";
                var labelY = Math.Max(0, y - 24);
                ctx.DrawText(label, font, color, new PointF(x, labelY));
            }
        });
    }
}
