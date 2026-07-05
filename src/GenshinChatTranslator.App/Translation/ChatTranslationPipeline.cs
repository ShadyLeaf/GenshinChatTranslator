using System.Security.Cryptography;
using System.Text;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Services;
using GenshinChatTranslator.App.Translation.Engines;

namespace GenshinChatTranslator.App.Translation;

public sealed class ChatTranslationPipeline
{
    private const int MaxSingleMessageConcurrency = 10;
    private const int SingleMessageCacheCapacity = 300;
    private const int ContextCacheCapacity = 50;

    private readonly TranslationOptions _options;
    private readonly TranslationCache _singleMessageCache = new(SingleMessageCacheCapacity);
    private readonly TranslationCache _contextCache = new(ContextCacheCapacity);
    private readonly IReadOnlyDictionary<TranslationEngineKind, ITranslationEngine> _engines;
    private readonly SemaphoreSlim _singleMessageConcurrencyGate = new(MaxSingleMessageConcurrency);
    private readonly object _settingsGate = new();
    private TranslationEngineKind _selectedEngineKind;
    private ChatLanguage _targetLanguage;

    public ChatTranslationPipeline(TranslationOptions options)
    {
        _options = options;
        _engines = BuildEngines(options);
        _selectedEngineKind = options.SelectedEngine == TranslationEngineKind.None || _engines.ContainsKey(options.SelectedEngine)
            ? options.SelectedEngine
            : _engines.Keys.FirstOrDefault();
        _targetLanguage = options.TargetLanguage;
    }

    public TranslationEngineKind SelectedEngineKind
    {
        get
        {
            lock (_settingsGate)
            {
                return _selectedEngineKind;
            }
        }
    }

    public ChatLanguage TargetLanguage
    {
        get
        {
            lock (_settingsGate)
            {
                return _targetLanguage;
            }
        }
    }

    public IReadOnlyList<TranslationEngineKind> AvailableEngineKinds => _engines.Keys.Prepend(TranslationEngineKind.None).Distinct().ToArray();

    public bool CanTranslateSelectedEnginePerItem
    {
        get
        {
            var engineKind = SelectedEngineKind;
            return engineKind != TranslationEngineKind.None &&
                _engines.TryGetValue(engineKind, out var engine) &&
                engine is ISingleMessageTranslationEngine;
        }
    }

    internal async Task<LlmConnectionValidationResult> ValidateSelectedLlmConnectionAsync(CancellationToken cancellationToken)
    {
        var engineKind = SelectedEngineKind;
        if (engineKind != TranslationEngineKind.OpenAiCompatibleLlm)
        {
            return LlmConnectionValidationResult.Success();
        }

        if (!_options.Enabled)
        {
            return LlmConnectionValidationResult.Failure("Translation is disabled. Enable translation before using LLM translation.");
        }

        if (!_options.OpenAiCompatibleLlm.Enabled)
        {
            return LlmConnectionValidationResult.Failure("OpenAI-compatible LLM translation is disabled. Enable translation.engines.openai_compatible_llm in config/translation.yml.");
        }

        return await OpenAiCompatibleLlmTranslateEngine.ValidateConnectionAsync(
            _options.OpenAiCompatibleLlm,
            _options.TimeoutMs,
            cancellationToken).ConfigureAwait(false);
    }

    public void SelectEngine(TranslationEngineKind engineKind)
    {
        lock (_settingsGate)
        {
            if (_selectedEngineKind == engineKind)
            {
                return;
            }

            _selectedEngineKind = engineKind;
            ClearCaches();
        }
    }

    public void SelectTargetLanguage(ChatLanguage language)
    {
        if (language == ChatLanguage.Auto)
        {
            return;
        }

        lock (_settingsGate)
        {
            if (_targetLanguage == language)
            {
                return;
            }

            _targetLanguage = language;
            ClearCaches();
        }
    }

    public async Task<IReadOnlyList<ChatTranslationItem>> TranslateAsync(
        IReadOnlyList<ChatOcrItem> ocrItems,
        CancellationToken cancellationToken)
    {
        var batch = await TranslateWithStatsAsync(ocrItems, cancellationToken).ConfigureAwait(false);
        return batch.Items;
    }

    public async Task<ChatTranslationBatch> TranslateWithStatsAsync(
        IReadOnlyList<ChatOcrItem> ocrItems,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled || ocrItems.Count == 0)
        {
            return ChatTranslationBatch.Empty;
        }

        var engineKind = SelectedEngineKind;
        if (engineKind == TranslationEngineKind.None)
        {
            return ChatTranslationBatch.Empty;
        }

        var targetLanguage = TargetLanguage;
        if (!_engines.TryGetValue(engineKind, out var engine))
        {
            return new ChatTranslationBatch(
                FailedForAll(ocrItems, engineKind, targetLanguage, $"Selected translation engine is not enabled: {engineKind}. Check config/translation.yml."),
                RequestSent: false);
        }

        var messages = BuildMessages(
            ocrItems,
            targetLanguage,
            engine,
            skipSameLanguage: _options.SkipSameLanguage && engine is not IContextTranslationEngine);
        if (messages.Count == 0)
        {
            return ChatTranslationBatch.Empty;
        }

        if (engine is IContextTranslationEngine contextEngine)
        {
            return await TranslateContextAsync(contextEngine, messages, targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        if (engine is ISingleMessageTranslationEngine singleEngine)
        {
            return await TranslateSingleMessagesAsync(singleEngine, messages, targetLanguage, cancellationToken).ConfigureAwait(false);
        }

        return new ChatTranslationBatch(
            FailedForMessages(messages, engineKind, targetLanguage, "Unsupported translation engine shape."),
            RequestSent: false);
    }

    public async Task<ChatTranslationBatch> TranslateSingleWithStatsAsync(
        ChatOcrItem ocrItem,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ChatTranslationBatch.Empty;
        }

        var engineKind = SelectedEngineKind;
        if (engineKind == TranslationEngineKind.None)
        {
            return ChatTranslationBatch.Empty;
        }

        var targetLanguage = TargetLanguage;
        if (!_engines.TryGetValue(engineKind, out var engine))
        {
            return new ChatTranslationBatch(
                FailedForAll([ocrItem], engineKind, targetLanguage, $"Selected translation engine is not enabled: {engineKind}. Check config/translation.yml."),
                RequestSent: false);
        }

        if (engine is not ISingleMessageTranslationEngine singleEngine)
        {
            return ChatTranslationBatch.Empty;
        }

        var messages = BuildMessages(
            [ocrItem],
            targetLanguage,
            engine,
            skipSameLanguage: _options.SkipSameLanguage);
        if (messages.Count == 0)
        {
            return ChatTranslationBatch.Empty;
        }

        return await TranslateSingleMessagesAsync(singleEngine, messages, targetLanguage, cancellationToken).ConfigureAwait(false);
    }

    private List<PreparedTranslationMessage> BuildMessages(
        IReadOnlyList<ChatOcrItem> ocrItems,
        ChatLanguage targetLanguage,
        ITranslationEngine engine,
        bool skipSameLanguage)
    {
        var messages = new List<PreparedTranslationMessage>();
        foreach (var item in ocrItems)
        {
            if (!item.Result.IsSuccess)
            {
                continue;
            }

            var text = TranslationTextNormalizer.Normalize(item.Result.Text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var sourceLanguage = item.Result.DetectedLanguage;
            if (skipSameLanguage && sourceLanguage == targetLanguage)
            {
                continue;
            }

            if (!engine.Supports(sourceLanguage, targetLanguage))
            {
                continue;
            }

            messages.Add(new PreparedTranslationMessage(
                item.Index,
                text,
                item.Roi.Kind == ChatRoiDetector.SelfLightKind ? "self" : "other",
                sourceLanguage));
        }

        return messages;
    }

    private async Task<ChatTranslationBatch> TranslateSingleMessagesAsync(
        ISingleMessageTranslationEngine engine,
        IReadOnlyList<PreparedTranslationMessage> messages,
        ChatLanguage targetLanguage,
        CancellationToken cancellationToken)
    {
        var tasks = messages
            .Select(message => TranslateSingleMessageAsync(engine, message, targetLanguage, cancellationToken))
            .ToArray();
        var outcomes = await Task.WhenAll(tasks).ConfigureAwait(false);
        return new ChatTranslationBatch(
            outcomes.Select(outcome => outcome.Item).ToArray(),
            outcomes.Any(outcome => outcome.RequestSent));
    }

    private async Task<SingleTranslationOutcome> TranslateSingleMessageAsync(
        ISingleMessageTranslationEngine engine,
        PreparedTranslationMessage message,
        ChatLanguage targetLanguage,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cacheKey = MakeSingleCacheKey(engine.Kind, targetLanguage, message);
        if (_options.CacheEnabled && _singleMessageCache.TryGet(cacheKey, out var cached))
        {
            return new SingleTranslationOutcome(
                new ChatTranslationItem(message.Index, message.Text, message.Speaker, true, cached),
                RequestSent: false);
        }

        await _singleMessageConcurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_options.CacheEnabled && _singleMessageCache.TryGet(cacheKey, out cached))
            {
                return new SingleTranslationOutcome(
                    new ChatTranslationItem(message.Index, message.Text, message.Speaker, true, cached),
                    RequestSent: false);
            }

            var request = new TranslationRequest(
                message.Index,
                message.Text,
                message.Speaker,
                message.SourceLanguage,
                targetLanguage,
                cacheKey);
            var result = await engine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            if (_options.CacheEnabled)
            {
                _singleMessageCache.Set(cacheKey, result);
            }

            return new SingleTranslationOutcome(
                new ChatTranslationItem(message.Index, message.Text, message.Speaker, false, result),
                RequestSent: true);
        }
        finally
        {
            _singleMessageConcurrencyGate.Release();
        }
    }

    private async Task<ChatTranslationBatch> TranslateContextAsync(
        IContextTranslationEngine engine,
        IReadOnlyList<PreparedTranslationMessage> messages,
        ChatLanguage targetLanguage,
        CancellationToken cancellationToken)
    {
        var cacheKey = MakeContextCacheKey(engine.Kind, targetLanguage, messages);
        if (_options.CacheEnabled && _contextCache.TryGet(cacheKey, out var cachedBatchMarker))
        {
            return new ChatTranslationBatch(DecodeCachedBatch(messages, cachedBatchMarker, targetLanguage), RequestSent: false);
        }

        var request = new ContextTranslationRequest(
            messages.Select(message => new ContextTranslationMessage(
                message.Index,
                message.Text,
                message.Speaker,
                message.SourceLanguage)).ToArray(),
            targetLanguage,
            cacheKey);
        var results = await engine.TranslateContextAsync(request, cancellationToken).ConfigureAwait(false);
        var byIndex = results.ToDictionary(item => item.Index, item => item.Result);
        var items = messages
            .Select(message =>
            {
                var result = byIndex.TryGetValue(message.Index, out var translated)
                    ? translated
                    : TranslationResult.Failure(engine.Kind, message.SourceLanguage, targetLanguage, TimeSpan.Zero, "Missing translation result.");
                return new ChatTranslationItem(message.Index, message.Text, message.Speaker, false, result);
            })
            .ToArray();

        if (_options.CacheEnabled && items.All(item => item.Result.IsSuccess))
        {
            _contextCache.Set(cacheKey, EncodeCachedBatch(items, engine.Kind, targetLanguage));
        }

        return new ChatTranslationBatch(items, RequestSent: true);
    }

    private void ClearCaches()
    {
        _singleMessageCache.Clear();
        _contextCache.Clear();
    }

    private static IReadOnlyList<ChatTranslationItem> DecodeCachedBatch(
        IReadOnlyList<PreparedTranslationMessage> messages,
        TranslationResult cachedBatchMarker,
        ChatLanguage targetLanguage)
    {
        var lines = cachedBatchMarker.Text.Split('\n');
        var translations = new Dictionary<int, string>();
        foreach (var line in lines)
        {
            var separator = line.IndexOf('\t');
            if (separator <= 0 || !int.TryParse(line[..separator], out var index))
            {
                continue;
            }

            translations[index] = line[(separator + 1)..];
        }

        return messages.Select(message =>
        {
            var text = translations.TryGetValue(message.Index, out var translated) ? translated : string.Empty;
            var result = string.IsNullOrWhiteSpace(text)
                ? TranslationResult.Failure(cachedBatchMarker.Engine, message.SourceLanguage, targetLanguage, TimeSpan.Zero, "Missing cached LLM translation.")
                : new TranslationResult(true, text, cachedBatchMarker.Engine, message.SourceLanguage, targetLanguage, TimeSpan.Zero);
            return new ChatTranslationItem(message.Index, message.Text, message.Speaker, true, result);
        }).ToArray();
    }

    private static TranslationResult EncodeCachedBatch(
        IReadOnlyList<ChatTranslationItem> items,
        TranslationEngineKind engineKind,
        ChatLanguage targetLanguage)
    {
        var text = string.Join('\n', items.Select(item => $"{item.Index}\t{item.Result.Text.ReplaceLineEndings(" ")}"));
        return new TranslationResult(true, text, engineKind, ChatLanguage.Auto, targetLanguage, TimeSpan.Zero);
    }

    private static IReadOnlyList<ChatTranslationItem> FailedForAll(
        IReadOnlyList<ChatOcrItem> ocrItems,
        TranslationEngineKind engineKind,
        ChatLanguage targetLanguage,
        string errorMessage)
    {
        return ocrItems
            .Where(item => item.Result.IsSuccess && !string.IsNullOrWhiteSpace(item.Result.Text))
            .Select(item => new ChatTranslationItem(
                item.Index,
                item.Result.Text,
                item.Roi.Kind == ChatRoiDetector.SelfLightKind ? "self" : "other",
                false,
                TranslationResult.Failure(engineKind, item.Result.DetectedLanguage, targetLanguage, TimeSpan.Zero, errorMessage)))
            .ToArray();
    }

    private static IReadOnlyList<ChatTranslationItem> FailedForMessages(
        IReadOnlyList<PreparedTranslationMessage> messages,
        TranslationEngineKind engineKind,
        ChatLanguage targetLanguage,
        string errorMessage)
    {
        return messages
            .Select(message => new ChatTranslationItem(
                message.Index,
                message.Text,
                message.Speaker,
                false,
                TranslationResult.Failure(engineKind, message.SourceLanguage, targetLanguage, TimeSpan.Zero, errorMessage)))
            .ToArray();
    }

    private static string MakeSingleCacheKey(
        TranslationEngineKind engineKind,
        ChatLanguage targetLanguage,
        PreparedTranslationMessage message)
    {
        return Hash($"{engineKind}|single|{message.SourceLanguage}|{targetLanguage}|{message.Text}");
    }

    private static string MakeContextCacheKey(
        TranslationEngineKind engineKind,
        ChatLanguage targetLanguage,
        IReadOnlyList<PreparedTranslationMessage> messages)
    {
        var builder = new StringBuilder();
        builder.Append(engineKind).Append("|context|").Append(targetLanguage);
        foreach (var message in messages)
        {
            builder
                .Append('|')
                .Append(message.Index)
                .Append(':')
                .Append(message.Speaker)
                .Append(':')
                .Append(message.SourceLanguage)
                .Append(':')
                .Append(message.Text);
        }

        return Hash(builder.ToString());
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static IReadOnlyDictionary<TranslationEngineKind, ITranslationEngine> BuildEngines(TranslationOptions options)
    {
        var engines = new List<(int Priority, ITranslationEngine Engine)>();
        if (options.MicrosoftEdge.Enabled)
        {
            engines.Add((options.MicrosoftEdge.Priority, new MicrosoftEdgeTranslateEngine(options.MicrosoftEdge, options.TimeoutMs)));
        }

        if (options.OpenAiCompatibleLlm.Enabled)
        {
            engines.Add((options.OpenAiCompatibleLlm.Priority, new OpenAiCompatibleLlmTranslateEngine(options.OpenAiCompatibleLlm, options.TimeoutMs)));
        }

        return engines
            .OrderBy(item => item.Priority)
            .Select(item => item.Engine)
            .ToDictionary(engine => engine.Kind);
    }

    private sealed record PreparedTranslationMessage(
        int Index,
        string Text,
        string Speaker,
        ChatLanguage SourceLanguage);

    private sealed record SingleTranslationOutcome(
        ChatTranslationItem Item,
        bool RequestSent);
}
