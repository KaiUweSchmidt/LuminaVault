extern alias FaceRec;

using FaceRec::LuminaVault.FaceRecognition;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace LuminaVault.Pipeline.Tests;

/// <summary>
/// Shared fixture that loads the YOLO ONNX model once for all visual tests.
/// </summary>
public sealed class YoloFaceDetectorFixture : IDisposable
{
    private static readonly string SolutionRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static readonly string ModelPath =
        Path.Combine(SolutionRoot, "models", "yolov12m-face.onnx");

    public YoloFaceDetector? Detector { get; }
    public bool IsAvailable => Detector is not null;

    public YoloFaceDetectorFixture()
    {
        if (!File.Exists(ModelPath))
            return;

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<YoloFaceDetector>();
        Detector = new YoloFaceDetector(ModelPath, logger);
    }

    public void Dispose() => Detector?.Dispose();
}

/// <summary>
/// Visual debugging test: runs YOLO face detection and draws bounding boxes onto a copy of the image.
/// Requires the ONNX model and a test image on disk — marked as Slow/Manual.
/// </summary>
public class YoloFaceDetectorVisualTests : IClassFixture<YoloFaceDetectorFixture>
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "Testdaten");
    private static readonly string OutputDir = Path.Combine(TestDataDir, "output");

    private readonly YoloFaceDetectorFixture _fixture;

    public YoloFaceDetectorVisualTests(YoloFaceDetectorFixture fixture)
    {
        _fixture = fixture;
    }

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
        if (!_fixture.IsAvailable)
            Assert.Skip("ONNX model not available");

        var imageBytes = File.ReadAllBytes(imagePath);
        var faces = _fixture.Detector!.DetectFaces(imageBytes);

        var fileName = Path.GetFileNameWithoutExtension(imagePath);
        Directory.CreateDirectory(OutputDir);

        using var image = Image.Load<Rgba32>(imageBytes);

        DrawBoxes(image, faces, image.Width, image.Height);
        image.SaveAsJpeg(Path.Combine(OutputDir, $"yolo_bbox_{fileName}.jpg"));
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
