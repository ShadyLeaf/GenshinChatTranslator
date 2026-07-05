using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation.Engines;

internal sealed class OpenAiCompatibleLlmTranslateEngine : IContextTranslationEngine
{
    private static readonly HttpClient HttpClient = new();
    private readonly OpenAiCompatibleLlmTranslationOptions _options;
    private readonly int _timeoutMs;

    public OpenAiCompatibleLlmTranslateEngine(OpenAiCompatibleLlmTranslationOptions options, int timeoutMs)
    {
        _options = options;
        _timeoutMs = timeoutMs;
    }

    public TranslationEngineKind Kind => TranslationEngineKind.OpenAiCompatibleLlm;

    public bool Supports(ChatLanguage sourceLanguage, ChatLanguage targetLanguage)
    {
        return targetLanguage is ChatLanguage.ChineseSimplified or ChatLanguage.English or ChatLanguage.Japanese &&
            sourceLanguage is ChatLanguage.Auto or ChatLanguage.ChineseSimplified or ChatLanguage.English or ChatLanguage.Japanese;
    }

    internal static async Task<LlmConnectionValidationResult> ValidateConnectionAsync(
        OpenAiCompatibleLlmTranslationOptions options,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            var apiKey = ResolveApiKey(options);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return LlmConnectionValidationResult.Failure("OpenAI-compatible LLM API key is empty.");
            }

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                return LlmConnectionValidationResult.Failure("OpenAI-compatible LLM endpoint is empty.");
            }

            if (string.IsNullOrWhiteSpace(options.Model))
            {
                return LlmConnectionValidationResult.Failure("OpenAI-compatible LLM model is empty.");
            }

            var payload = JsonSerializer.Serialize(new
            {
                model = options.Model.Trim(),
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = "ping",
                    },
                },
                temperature = 0,
                max_tokens = 4,
                stream = false,
            });

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, options.Endpoint.Trim());
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 GenshinChatTranslator/0.1");
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var timeout = new CancellationTokenSource(Math.Max(1000, timeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await HttpClient.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return LlmConnectionValidationResult.Failure($"OpenAI-compatible LLM HTTP {(int)response.StatusCode}: {TrimSnippet(body)}");
            }

            if (!LooksLikeOpenAiChatCompletion(body))
            {
                return LlmConnectionValidationResult.Failure("OpenAI-compatible LLM response does not look like a chat completion.");
            }

            return LlmConnectionValidationResult.Success();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LlmConnectionValidationResult.Failure("OpenAI-compatible LLM validation timed out.");
        }
        catch (Exception ex)
        {
            return LlmConnectionValidationResult.Failure(ex.Message);
        }
    }

    public async Task<IReadOnlyList<ContextTranslationResult>> TranslateContextAsync(
        ContextTranslationRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                stopwatch.Stop();
                return FailAll(request, stopwatch.Elapsed, "OpenAI-compatible LLM API key is empty. Set translation.engines.openai_compatible_llm.api_key, OPENAI_API_KEY, or OPENAI_COMPATIBLE_API_KEY.");
            }

            var payload = BuildPayload(request);
            WriteRequestSummary(request);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 GenshinChatTranslator/0.1");
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var timeout = new CancellationTokenSource(Math.Max(1000, _timeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await HttpClient.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return FailAll(request, stopwatch.Elapsed, $"OpenAI-compatible LLM HTTP {(int)response.StatusCode}: {TrimSnippet(body)}");
            }

            var content = ExtractAssistantContent(body);
            var translations = ExtractTranslations(content);
            if (translations.Count == 0)
            {
                return FailAll(request, stopwatch.Elapsed, "OpenAI-compatible LLM returned no structured translations.");
            }

            return request.Messages
                .Select(message =>
                {
                    var text = translations.TryGetValue(message.Index, out var translated)
                        ? translated
                        : string.Empty;
                    var result = string.IsNullOrWhiteSpace(text)
                        ? TranslationResult.Failure(Kind, message.SourceLanguage, request.TargetLanguage, stopwatch.Elapsed, "Missing translation for this ROI in LLM response.")
                        : new TranslationResult(true, text.Trim(), Kind, message.SourceLanguage, request.TargetLanguage, stopwatch.Elapsed);
                    return new ContextTranslationResult(message.Index, result);
                })
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return FailAll(request, stopwatch.Elapsed, ex.Message);
        }
    }

    private string BuildPayload(ContextTranslationRequest request)
    {
        var context = request.Messages.Select(message => new
        {
            index = message.Index,
            speaker = message.Speaker,
            source_language = message.SourceLanguage.ToString(),
            text = message.Text,
        });

        var userContent = JsonSerializer.Serialize(new
        {
            game = "Genshin Impact",
            instruction = "Translate the entire visible in-game chat context. Speaker 'self' means messages sent by the player; speaker 'other' means messages from the other player. Use the context to resolve pronouns, slang, game terms, and short replies.",
            target_language = TranslationLanguageMapper.OpenAiPromptName(request.TargetLanguage),
            messages = context,
            output_schema = new
            {
                translations = new[]
                {
                    new { index = 1, translation = "translated text" },
                },
            },
        });

        var payload = new
        {
            model = _options.Model,
            temperature = _options.Temperature,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a professional game chat translator for Genshin Impact. Return only a JSON object. Do not add markdown, comments, explanations, or extra keys. Preserve message order and translate every item.",
                },
                new
                {
                    role = "user",
                    content = userContent,
                },
            },
            response_format = new { type = "json_object" },
            thinking = new { type = _options.ThinkingType },
        };

        return JsonSerializer.Serialize(payload);
    }

    private string ResolveApiKey()
    {
        return ResolveApiKey(_options);
    }

    private static string ResolveApiKey(OpenAiCompatibleLlmTranslationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return options.ApiKey.Trim();
        }

        return Environment.GetEnvironmentVariable("OPENAI_COMPATIBLE_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? string.Empty;
    }

    private void WriteRequestSummary(ContextTranslationRequest request)
    {
        var indices = CompactIndices(request.Messages.Select(message => message.Index).ToArray());
        var status = LlmTranslationRequestStatusStore.RecordSent(
            _options.Model,
            request.TargetLanguage,
            request.Messages.Count,
            request.Messages.Sum(message => message.Text.Length),
            indices,
            _options.ThinkingType);
        var summary = BuildRequestSummary(status);
        Console.WriteLine(summary);
        Debug.WriteLine(summary);
    }

    private static string BuildRequestSummary(LlmTranslationRequestStatus status)
    {
        return $"[{status.SentAt:HH:mm:ss}] LLM#{status.Sequence} sent: model={status.Model}, target={status.TargetLanguage}, msgs={status.MessageCount}, chars={status.CharacterCount}, roi={status.Indices}, thinking={status.ThinkingType}";
    }

    private static string CompactIndices(IReadOnlyList<int> indices)
    {
        if (indices.Count == 0)
        {
            return "-";
        }

        if (indices.Count <= 4)
        {
            return string.Join(",", indices);
        }

        return $"{indices[0]}..{indices[^1]}";
    }

    private static string ExtractAssistantContent(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textPart))
                {
                    builder.Append(textPart.GetString());
                }
            }

            return builder.ToString();
        }

        return string.Empty;
    }

    private static IReadOnlyDictionary<int, string> ExtractTranslations(string content)
    {
        var json = CleanJson(content);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<int, string>();
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<int, string>();
        }

        var results = new Dictionary<int, string>();
        foreach (var item in translations.EnumerateArray())
        {
            if (!item.TryGetProperty("index", out var indexElement) ||
                !indexElement.TryGetInt32(out var index) ||
                !item.TryGetProperty("translation", out var translationElement))
            {
                continue;
            }

            var text = translationElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                results[index] = text.Trim();
            }
        }

        return results;
    }

    private static string CleanJson(string text)
    {
        var cleaned = text.Trim();
        if (!cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            return cleaned;
        }

        var firstNewLine = cleaned.IndexOf('\n');
        var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine >= 0 && lastFence > firstNewLine)
        {
            return cleaned[(firstNewLine + 1)..lastFence].Trim();
        }

        return cleaned.Trim('`').Trim();
    }

    private static bool LooksLikeOpenAiChatCompletion(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        var firstChoice = choices[0];
        return firstChoice.TryGetProperty("message", out _) ||
            firstChoice.TryGetProperty("delta", out _) ||
            firstChoice.TryGetProperty("text", out _);
    }

    private IReadOnlyList<ContextTranslationResult> FailAll(
        ContextTranslationRequest request,
        TimeSpan duration,
        string errorMessage)
    {
        return request.Messages
            .Select(message => new ContextTranslationResult(
                message.Index,
                TranslationResult.Failure(Kind, message.SourceLanguage, request.TargetLanguage, duration, errorMessage)))
            .ToArray();
    }

    private static string TrimSnippet(string text)
    {
        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}

internal sealed record LlmConnectionValidationResult(bool IsSuccess, string ErrorMessage)
{
    public static LlmConnectionValidationResult Success()
    {
        return new LlmConnectionValidationResult(true, string.Empty);
    }

    public static LlmConnectionValidationResult Failure(string errorMessage)
    {
        return new LlmConnectionValidationResult(false, errorMessage);
    }
}
