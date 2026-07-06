using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Ocr.Engines;

namespace GenshinChatTranslator.App.Ocr;

public sealed class ChatOcrPipeline
{
    private readonly OcrOptions _options;
    private readonly OcrImagePreprocessor _preprocessor;
    private readonly OcrResultCache _cache = new();
    private readonly IReadOnlyDictionary<OcrEngineKind, IChatOcrEngine> _engines;
    private readonly object _engineGate = new();
    private readonly object _runtimeSettingsGate = new();
    private OcrEngineKind _selectedEngineKind;
    private int _runtimeMaxConcurrency;

    public ChatOcrPipeline(OcrOptions options)
    {
        _options = options;
        _preprocessor = new OcrImagePreprocessor(options);
        _engines = BuildEngines(options);
        _selectedEngineKind = _engines.ContainsKey(options.SelectedEngine)
            ? options.SelectedEngine
            : _engines.Keys.FirstOrDefault();
        _runtimeMaxConcurrency = options.MaxConcurrency;
    }

    public OcrEngineKind SelectedEngineKind
    {
        get
        {
            lock (_engineGate)
            {
                return _selectedEngineKind;
            }
        }
    }

    public IReadOnlyList<OcrEngineKind> AvailableEngineKinds => _engines.Keys.ToArray();

    public int RuntimeMaxConcurrency
    {
        get
        {
            lock (_runtimeSettingsGate)
            {
                return _runtimeMaxConcurrency;
            }
        }
    }

    public void SetSerialOcr(bool enabled)
    {
        SetRuntimeMaxConcurrency(enabled ? 1 : _options.MaxConcurrency);
    }

    public void SetRuntimeMaxConcurrency(int maxConcurrency)
    {
        lock (_runtimeSettingsGate)
        {
            _runtimeMaxConcurrency = Math.Max(1, maxConcurrency);
        }
    }

    public void SelectEngine(OcrEngineKind engineKind)
    {
        lock (_engineGate)
        {
            if (_selectedEngineKind == engineKind)
            {
                return;
            }

            _selectedEngineKind = engineKind;
            _cache.Clear();
        }
    }

    public async Task<IReadOnlyList<ChatOcrItem>> RecognizeAsync(
        RgbFrame frame,
        IReadOnlyList<ChatBubbleRoi> rois,
        CancellationToken cancellationToken,
        Action<ChatOcrItem>? itemRecognized = null)
    {
        if (!_options.Enabled || rois.Count == 0)
        {
            return Array.Empty<ChatOcrItem>();
        }

        var engineKind = SelectedEngineKind;
        var items = new ChatOcrItem[rois.Count];
        using var concurrencyGate = new SemaphoreSlim(Math.Min(RuntimeMaxConcurrency, rois.Count));
        var pendingTasks = new List<Task>(rois.Count);

        for (var index = 0; index < rois.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var roi = rois[index];
            var prepared = _preprocessor.Prepare(frame, roi);
            OcrResult? cached = null;
            var cacheKey = BuildCacheKey(engineKind, prepared.ImageHash);
            var cacheHit = _options.SkipUnchangedImage && _cache.TryGet(cacheKey, out cached);
            if (cacheHit)
            {
                items[index] = BuildItem(index, roi, prepared, cached!, cacheHit: true);
                itemRecognized?.Invoke(items[index]);
                continue;
            }

            pendingTasks.Add(RecognizeAndStoreAsync(index, frame, roi, prepared, engineKind, cacheKey, items, concurrencyGate, cancellationToken, itemRecognized));
        }

        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        return items;
    }

    private async Task RecognizeAndStoreAsync(
        int index,
        RgbFrame frame,
        ChatBubbleRoi roi,
        OcrPreparedImage prepared,
        OcrEngineKind engineKind,
        string cacheKey,
        ChatOcrItem[] items,
        SemaphoreSlim concurrencyGate,
        CancellationToken cancellationToken,
        Action<ChatOcrItem>? itemRecognized)
    {
        await concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await RecognizeOneAsync(frame, roi, prepared, engineKind, cancellationToken).ConfigureAwait(false);
            _cache.Set(cacheKey, result);
            items[index] = BuildItem(index, roi, prepared, result, cacheHit: false);
            itemRecognized?.Invoke(items[index]);
        }
        finally
        {
            concurrencyGate.Release();
        }
    }

    private async Task<OcrResult> RecognizeOneAsync(
        RgbFrame frame,
        ChatBubbleRoi roi,
        OcrPreparedImage prepared,
        OcrEngineKind engineKind,
        CancellationToken cancellationToken)
    {
        if (!_engines.TryGetValue(engineKind, out var engine))
        {
            return OcrResult.Failure(
                engineKind == default ? OcrEngineKind.Stub : engineKind,
                TimeSpan.Zero,
                $"Selected OCR engine is not enabled: {engineKind}. Check config/ocr.yml.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!engine.SupportedLanguages.Contains(_options.LanguageHint))
        {
            return OcrResult.Failure(engine.Kind, TimeSpan.Zero, $"{engine.Kind}: unsupported language hint {_options.LanguageHint}");
        }

        var request = new OcrRequest(
            frame,
            roi,
            _options.LanguageHint,
            prepared.Profile,
            prepared.ImageHash,
            prepared.PreparedImage);
        var result = NormalizeResult(await engine.RecognizeAsync(request, cancellationToken).ConfigureAwait(false));
        if (result.IsSuccess &&
            result.Confidence < _options.MinConfidence &&
            !string.IsNullOrWhiteSpace(result.Text))
        {
            return result with
            {
                IsSuccess = false,
                ErrorMessage = $"{engine.Kind}: confidence {result.Confidence:0.000} is below {_options.MinConfidence:0.000}",
            };
        }

        return result;
    }

    private static ChatOcrItem BuildItem(
        int index,
        ChatBubbleRoi roi,
        OcrPreparedImage prepared,
        OcrResult result,
        bool cacheHit)
    {
        return new ChatOcrItem(
            index + 1,
            roi,
            prepared.InputImage,
            prepared.PreparedImage,
            prepared.ImageHash,
            result,
            cacheHit);
    }

    private static string BuildCacheKey(OcrEngineKind engineKind, string imageHash)
    {
        return $"{engineKind}:{imageHash}";
    }

    private static OcrResult NormalizeResult(OcrResult result)
    {
        if (!result.IsSuccess)
        {
            return result;
        }

        var text = OcrTextNormalizer.Normalize(result.Text);
        var language = result.DetectedLanguage == ChatLanguage.Auto
            ? OcrTextNormalizer.GuessLanguage(text)
            : result.DetectedLanguage;
        return result with
        {
            Text = text,
            DetectedLanguage = language,
            IsSuccess = true,
            ErrorMessage = null,
        };
    }

    private static IReadOnlyDictionary<OcrEngineKind, IChatOcrEngine> BuildEngines(OcrOptions options)
    {
        var engines = new List<(int Priority, IChatOcrEngine Engine)>();
        if (options.Paddle.Enabled)
        {
            engines.Add((options.Paddle.Priority, new PaddleOcrEngine(options.Paddle)));
        }

        if (options.Windows.Enabled)
        {
            engines.Add((options.Windows.Priority, new WindowsOcrEngine(options.Windows)));
        }

        if (options.OpenAiVision.Enabled)
        {
            engines.Add((options.OpenAiVision.Priority, new OpenAiVisionOcrEngine(options.OpenAiVision)));
        }

        if (options.WeChat.Enabled)
        {
            engines.Add((options.WeChat.Priority, new WeChatOcrEngine(options.WeChat)));
        }

        return engines
            .OrderBy(item => item.Priority)
            .Select(item => item.Engine)
            .ToDictionary(engine => engine.Kind);
    }
}
