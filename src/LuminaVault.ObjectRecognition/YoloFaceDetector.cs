using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuminaVault.ObjectRecognition;

/// <summary>
/// Detects faces using a YOLOv8-face ONNX model. Returns bounding boxes as percentages (0-100) of original image dimensions.
/// </summary>
public sealed class YoloFaceDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly ILogger<YoloFaceDetector> _logger;
    private const int InputSize = 640;
    private const float ConfidenceThreshold = 0.45f;
    private const float NmsIouThreshold = 0.5f;

    public YoloFaceDetector(string modelPath, ILogger<YoloFaceDetector> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _logger = logger;
        _session = new InferenceSession(modelPath);
        _logger.LogInformation("[YOLO] Modell geladen: {ModelPath}", modelPath);
    }

    /// <summary>
    /// Detects faces in a base64-encoded image. Returns detected faces with bounding boxes as percentages.
    /// </summary>
    public List<DetectedFace> DetectFaces(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        using var image = Image.Load<Rgb24>(imageBytes);
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        // Compute letterbox parameters
        var scale = Math.Min((float)InputSize / originalWidth, (float)InputSize / originalHeight);
        var scaledWidth = (int)(originalWidth * scale);
        var scaledHeight = (int)(originalHeight * scale);
        var padX = (InputSize - scaledWidth) / 2;
        var padY = (InputSize - scaledHeight) / 2;
        // Use float padding for accurate reverse mapping (int truncation causes up to 0.5px shift)
        var padXf = (InputSize - scaledWidth) / 2f;
        var padYf = (InputSize - scaledHeight) / 2f;

        // Resize with letterbox
        image.Mutate(ctx => ctx.Resize(scaledWidth, scaledHeight));
        using var padded = new Image<Rgb24>(InputSize, InputSize, new Rgb24(114, 114, 114));
        padded.Mutate(ctx => ctx.DrawImage(image, new Point(padX, padY), 1f));

        // Create input tensor [1, 3, 640, 640] normalized to 0-1
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        padded.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < InputSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < InputSize; x++)
                {
                    var pixel = row[x];
                    tensor[0, 0, y, x] = pixel.R / 255f;
                    tensor[0, 1, y, x] = pixel.G / 255f;
                    tensor[0, 2, y, x] = pixel.B / 255f;
                }
            }
        });

        var inputName = _session.InputNames[0];
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, tensor) };

        using var results = _session.Run(inputs);
        var output = results.First().AsTensor<float>();

        // Output shape: [1, num_detections, num_attributes] where attributes = x, y, w, h, confidence, ...landmarks
        var outputShape = output.Dimensions.ToArray();
        var numDetections = outputShape[1];
        var numAttributes = outputShape[2];

        _logger.LogInformation("[YOLO] Modell-Output: {Dets} Detektionen x {Attrs} Attribute", numDetections, numAttributes);

        var candidates = new List<DetectedFace>();
        for (var d = 0; d < numDetections; d++)
        {
            var confidence = output[0, d, 4];
            if (confidence < ConfidenceThreshold) continue;

            // YOLO outputs center_x, center_y, width, height in input (640x640) letterboxed coordinates
            var cx = output[0, d, 0];
            var cy = output[0, d, 1];
            var w = output[0, d, 2];
            var h = output[0, d, 3];

            // Remove letterbox padding and scale back to original image coordinates
            var x1 = (cx - w / 2f - padXf) / scale;
            var y1 = (cy - h / 2f - padYf) / scale;
            var bw = w / scale;
            var bh = h / scale;

            candidates.Add(new DetectedFace(
                BboxX: Math.Clamp(x1 / originalWidth * 100, 0, 100),
                BboxY: Math.Clamp(y1 / originalHeight * 100, 0, 100),
                BboxWidth: Math.Clamp(bw / originalWidth * 100, 0, 100),
                BboxHeight: Math.Clamp(bh / originalHeight * 100, 0, 100),
                Confidence: confidence));
        }

        // Apply Non-Maximum Suppression
        var sorted = candidates.OrderByDescending(f => f.Confidence).ToList();
        var kept = new List<DetectedFace>();
        var suppressed = new HashSet<int>();

        for (var i = 0; i < sorted.Count; i++)
        {
            if (suppressed.Contains(i)) continue;
            kept.Add(sorted[i]);
            for (var j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed.Contains(j)) continue;
                if (ComputeIoU(sorted[i], sorted[j]) > NmsIouThreshold)
                    suppressed.Add(j);
            }
        }

        _logger.LogInformation("[YOLO] {Candidates} Kandidaten → {Kept} Gesichter nach NMS (Threshold={Threshold})",
            candidates.Count, kept.Count, ConfidenceThreshold);

        return kept;
    }

    private static float ComputeIoU(DetectedFace a, DetectedFace b)
    {
        var ax1 = a.BboxX; var ay1 = a.BboxY;
        var ax2 = a.BboxX + a.BboxWidth; var ay2 = a.BboxY + a.BboxHeight;
        var bx1 = b.BboxX; var by1 = b.BboxY;
        var bx2 = b.BboxX + b.BboxWidth; var by2 = b.BboxY + b.BboxHeight;

        var ix1 = Math.Max(ax1, bx1); var iy1 = Math.Max(ay1, by1);
        var ix2 = Math.Min(ax2, bx2); var iy2 = Math.Min(ay2, by2);

        var interW = Math.Max(0, ix2 - ix1);
        var interH = Math.Max(0, iy2 - iy1);
        var inter = interW * interH;

        var areaA = a.BboxWidth * a.BboxHeight;
        var areaB = b.BboxWidth * b.BboxHeight;
        var union = areaA + areaB - inter;

        return union > 0 ? (float)(inter / union) : 0;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>
/// A detected face with bounding box coordinates as percentages (0-100) of the original image.
/// </summary>
public record DetectedFace(
    double BboxX,
    double BboxY,
    double BboxWidth,
    double BboxHeight,
    float Confidence);
