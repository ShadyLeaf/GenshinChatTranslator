using System.Diagnostics;
using System.IO;
using GenshinChatTranslator.App.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace GenshinChatTranslator.App.Ocr.Engines;

internal sealed class PaddleOcrEngine : IChatOcrEngine, IDisposable
{
    private readonly PaddleOcrOptions _options;
    private readonly object _gate = new();
    private InferenceSession? _session;
    private string[]? _characters;
    private string? _inputName;
    private int _inputHeight;
    private int _inputWidth;

    public PaddleOcrEngine(PaddleOcrOptions options)
    {
        _options = options;
    }

    public OcrEngineKind Kind => OcrEngineKind.Paddle;

    public IReadOnlySet<ChatLanguage> SupportedLanguages { get; } =
        new HashSet<ChatLanguage>
        {
            ChatLanguage.Auto,
            ChatLanguage.ChineseSimplified,
            ChatLanguage.English,
            ChatLanguage.Japanese,
        };

    public Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureLoaded();
            cancellationToken.ThrowIfCancellationRequested();

            var session = _session!;
            var inputName = _inputName!;
            var input = BuildInputTensor(request.PreparedImage, _inputHeight, _inputWidth);
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, input),
            };
            using var outputs = session.Run(inputs);
            var output = outputs.First().AsTensor<float>();
            var decoded = Decode(output, _characters!);

            stopwatch.Stop();
            if (decoded.Text.Length == 0)
            {
                return Task.FromResult(OcrResult.Empty(Kind, stopwatch.Elapsed));
            }

            return Task.FromResult(new OcrResult(
                IsSuccess: true,
                Text: decoded.Text,
                Confidence: decoded.Confidence,
                DetectedLanguage: ChatLanguage.Auto,
                Engine: Kind,
                Duration: stopwatch.Elapsed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return Task.FromResult(OcrResult.Failure(Kind, stopwatch.Elapsed, ex.Message));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    private void EnsureLoaded()
    {
        if (_session is not null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not null)
            {
                return;
            }

            var (modelPath, dictionaryPath) = ResolveModelFiles();
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"PaddleOCR recognition model not found: {modelPath}");
            }

            if (!File.Exists(dictionaryPath))
            {
                throw new FileNotFoundException($"PaddleOCR dictionary not found: {dictionaryPath}");
            }

            _characters = File.ReadAllLines(dictionaryPath)
                .Where(line => line.Length > 0)
                .ToArray();

            var sessionOptions = new SessionOptions
            {
                IntraOpNumThreads = Math.Max(1, _options.CpuThreads),
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            _session = new InferenceSession(modelPath, sessionOptions);
            var input = _session.InputMetadata.First();
            _inputName = input.Key;
            var dimensions = input.Value.Dimensions.ToArray();
            _inputHeight = dimensions.Length >= 3 && dimensions[^2] > 0 ? dimensions[^2] : Math.Max(1, _options.InputHeight);
            _inputWidth = dimensions.Length >= 4 && dimensions[^1] > 0 ? dimensions[^1] : Math.Max(1, _options.MaxWidth);
        }
    }

    private (string ModelPath, string DictionaryPath) ResolveModelFiles()
    {
        var configuredModelPath = OcrPathResolver.ResolveWorkspacePath(_options.ModelPath);
        var configuredDictionaryPath = OcrPathResolver.ResolveWorkspacePath(_options.DictionaryPath);
        if (File.Exists(configuredModelPath) && File.Exists(configuredDictionaryPath))
        {
            return (configuredModelPath, configuredDictionaryPath);
        }

        var root = OcrPathResolver.ResolveWorkspacePath("models/ocr/paddle");
        if (Directory.Exists(root))
        {
            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                var modelPath = PickFirstExisting(
                    Path.Combine(directory, "rec.onnx"),
                    Path.Combine(directory, "ch_PP-OCRv5_mobile_rec.onnx"),
                    Path.Combine(directory, "ch_PP-OCRv5_server_rec.onnx"));
                modelPath ??= Directory.EnumerateFiles(directory, "*rec*.onnx", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                var dictionaryPath = PickFirstExisting(
                    Path.Combine(directory, "dict.txt"),
                    Path.Combine(directory, "ppocr_keys_v5.txt"));
                dictionaryPath ??= Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
                    .Where(path => Path.GetFileName(path).Contains("key", StringComparison.OrdinalIgnoreCase) ||
                                   Path.GetFileName(path).Contains("dict", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (modelPath is not null && dictionaryPath is not null)
                {
                    return (modelPath, dictionaryPath);
                }
            }
        }

        return (configuredModelPath, configuredDictionaryPath);
    }

    private static string? PickFirstExisting(params string[] paths)
    {
        return paths.FirstOrDefault(File.Exists);
    }

    private static DenseTensor<float> BuildInputTensor(RgbFrame image, int targetHeight, int targetWidth)
    {
        var resizedWidth = Math.Clamp((int)Math.Round(image.Width * (targetHeight / (double)Math.Max(1, image.Height))), 1, targetWidth);
        var tensor = new DenseTensor<float>(new[] { 1, 3, targetHeight, targetWidth });

        for (var channel = 0; channel < 3; channel++)
        {
            for (var y = 0; y < targetHeight; y++)
            {
                var sourceY = Math.Clamp((int)Math.Floor(y * image.Height / (double)targetHeight), 0, image.Height - 1);
                for (var x = 0; x < targetWidth; x++)
                {
                    var value = 255;
                    if (x < resizedWidth)
                    {
                        var sourceX = Math.Clamp((int)Math.Floor(x * image.Width / (double)resizedWidth), 0, image.Width - 1);
                        value = image.Pixels[image.PixelOffset(sourceX, sourceY) + channel];
                    }

                    tensor[0, channel, y, x] = (value / 255f - 0.5f) / 0.5f;
                }
            }
        }

        return tensor;
    }

    private static (string Text, double Confidence) Decode(Tensor<float> output, string[] characters)
    {
        var dimensions = output.Dimensions.ToArray();
        if (dimensions.Length < 3)
        {
            throw new InvalidDataException($"Unsupported PaddleOCR output rank: {dimensions.Length}");
        }

        var batch = dimensions[0];
        var timeSteps = dimensions[1];
        var classes = dimensions[2];
        if (batch != 1)
        {
            throw new InvalidDataException($"Unsupported PaddleOCR batch size: {batch}");
        }

        var text = new List<string>();
        var probabilities = new List<double>();
        var previousIndex = -1;
        for (var timestep = 0; timestep < timeSteps; timestep++)
        {
            var bestIndex = 0;
            var bestValue = output[0, timestep, 0];
            for (var classIndex = 1; classIndex < classes; classIndex++)
            {
                var value = output[0, timestep, classIndex];
                if (value > bestValue)
                {
                    bestValue = value;
                    bestIndex = classIndex;
                }
            }

            if (bestIndex > 0 && bestIndex != previousIndex)
            {
                var characterIndex = bestIndex - 1;
                if (characterIndex < characters.Length)
                {
                    text.Add(characters[characterIndex]);
                    probabilities.Add(bestValue);
                }
            }

            previousIndex = bestIndex;
        }

        var confidence = probabilities.Count == 0 ? 0 : probabilities.Average();
        return (string.Concat(text), confidence);
    }
}
