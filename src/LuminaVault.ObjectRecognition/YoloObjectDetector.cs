using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LuminaVault.ObjectRecognition;

/// <summary>
/// Detects objects using a YOLOv8 ONNX model with 80 COCO classes.
/// Returns distinct object labels found in the image.
/// </summary>
public sealed class YoloObjectDetector : IDisposable
{
    private readonly InferenceSession _session;
    private readonly ILogger<YoloObjectDetector> _logger;
    private const int InputSize = 640;
    private const float ConfidenceThreshold = 0.45f;
    private const float NmsIouThreshold = 0.5f;

    private static readonly string[] CocoClasses =
    [
        "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
        "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
        "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
        "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
        "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
        "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
        "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair",
        "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
        "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator",
        "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
    ];

    public YoloObjectDetector(string modelPath, ILogger<YoloObjectDetector> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        _logger = logger;
        _session = new InferenceSession(modelPath);
        _logger.LogInformation("[YOLO-Obj] Modell geladen: {ModelPath}", modelPath);
    }

    /// <summary>
    /// Detects objects in the given image bytes and returns distinct labels and whether a person was detected.
    /// </summary>
    public YoloDetectionResult Detect(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        using var image = Image.Load<Rgb24>(imageBytes);
        var originalWidth = image.Width;
        var originalHeight = image.Height;

        var scale = Math.Min((float)InputSize / originalWidth, (float)InputSize / originalHeight);
        var scaledWidth = (int)(originalWidth * scale);
        var scaledHeight = (int)(originalHeight * scale);
        var padX = (InputSize - scaledWidth) / 2;
        var padY = (InputSize - scaledHeight) / 2;

        image.Mutate(ctx => ctx.Resize(scaledWidth, scaledHeight));
        using var padded = new Image<Rgb24>(InputSize, InputSize, new Rgb24(114, 114, 114));
        padded.Mutate(ctx => ctx.DrawImage(image, new Point(padX, padY), 1f));

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

        // YOLOv8 output: [1, 84, N] where 84 = 4 (cx,cy,w,h) + 80 (class scores)
        var dims = output.Dimensions.ToArray();
        var numAttributes = dims[1];
        var numDetections = dims[2];
        var numClasses = numAttributes - 4;

        _logger.LogInformation("[YOLO-Obj] Modell-Output: {Attrs} Attribute x {Dets} Detektionen", numAttributes, numDetections);

        var candidates = new List<(int ClassId, float Score, float Cx, float Cy, float W, float H)>();

        for (var d = 0; d < numDetections; d++)
        {
            var bestClassId = -1;
            var bestScore = 0f;

            for (var c = 0; c < numClasses; c++)
            {
                var score = output[0, 4 + c, d];
                if (score > bestScore)
                {
                    bestScore = score;
                    bestClassId = c;
                }
            }

            if (bestScore < ConfidenceThreshold) continue;

            candidates.Add((
                bestClassId,
                bestScore,
                output[0, 0, d],
                output[0, 1, d],
                output[0, 2, d],
                output[0, 3, d]));
        }

        // NMS per class
        var kept = new List<(int ClassId, float Score)>();
        foreach (var group in candidates.GroupBy(c => c.ClassId))
        {
            var sorted = group.OrderByDescending(c => c.Score).ToList();
            var suppressed = new HashSet<int>();

            for (var i = 0; i < sorted.Count; i++)
            {
                if (suppressed.Contains(i)) continue;
                kept.Add((sorted[i].ClassId, sorted[i].Score));
                for (var j = i + 1; j < sorted.Count; j++)
                {
                    if (suppressed.Contains(j)) continue;
                    if (ComputeIoU(sorted[i], sorted[j]) > NmsIouThreshold)
                        suppressed.Add(j);
                }
            }
        }

        var detectedLabels = kept
            .Select(k => CocoClasses[k.ClassId])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var personDetected = detectedLabels.Contains("person", StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("[YOLO-Obj] {Count} Objekte erkannt: [{Labels}], PersonDetected={PersonDetected}",
            detectedLabels.Count, string.Join(", ", detectedLabels), personDetected);

        return new YoloDetectionResult(detectedLabels, personDetected);
    }

    private static float ComputeIoU(
        (int ClassId, float Score, float Cx, float Cy, float W, float H) a,
        (int ClassId, float Score, float Cx, float Cy, float W, float H) b)
    {
        var ax1 = a.Cx - a.W / 2; var ay1 = a.Cy - a.H / 2;
        var ax2 = a.Cx + a.W / 2; var ay2 = a.Cy + a.H / 2;
        var bx1 = b.Cx - b.W / 2; var by1 = b.Cy - b.H / 2;
        var bx2 = b.Cx + b.W / 2; var by2 = b.Cy + b.H / 2;

        var ix1 = Math.Max(ax1, bx1); var iy1 = Math.Max(ay1, by1);
        var ix2 = Math.Min(ax2, bx2); var iy2 = Math.Min(ay2, by2);

        var interW = Math.Max(0, ix2 - ix1);
        var interH = Math.Max(0, iy2 - iy1);
        var inter = interW * interH;

        var areaA = a.W * a.H;
        var areaB = b.W * b.H;
        var union = areaA + areaB - inter;

        return union > 0 ? inter / union : 0;
    }

    public void Dispose() => _session.Dispose();
}
