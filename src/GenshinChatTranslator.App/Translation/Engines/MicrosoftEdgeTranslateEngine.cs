using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenshinChatTranslator.App.Ocr;

namespace GenshinChatTranslator.App.Translation.Engines;

internal sealed class MicrosoftEdgeTranslateEngine : ISingleMessageTranslationEngine
{
    private static readonly HttpClient HttpClient = new();
    private readonly MicrosoftEdgeTranslationOptions _options;
    private readonly int _timeoutMs;
    private readonly object _tokenGate = new();
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public MicrosoftEdgeTranslateEngine(MicrosoftEdgeTranslationOptions options, int timeoutMs)
    {
        _options = options;
        _timeoutMs = timeoutMs;
    }

    public TranslationEngineKind Kind => TranslationEngineKind.MicrosoftEdge;

    public bool Supports(ChatLanguage sourceLanguage, ChatLanguage targetLanguage)
    {
        return targetLanguage is ChatLanguage.ChineseSimplified or ChatLanguage.English or ChatLanguage.Japanese &&
            sourceLanguage is ChatLanguage.Auto or ChatLanguage.ChineseSimplified or ChatLanguage.English or ChatLanguage.Japanese;
    }

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            var query = new StringBuilder($"{_options.Endpoint}?api-version=3.0&to={Uri.EscapeDataString(TranslationLanguageMapper.Microsoft(request.TargetLanguage))}");
            if (request.SourceLanguage != ChatLanguage.Auto)
            {
                query.Append("&from=").Append(Uri.EscapeDataString(TranslationLanguageMapper.Microsoft(request.SourceLanguage)));
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, query.ToString());
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpRequest.Headers.UserAgent.ParseAdd("Mozilla/5.0 GenshinChatTranslator/0.1");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(new[] { new { Text = request.Text } }),
                Encoding.UTF8,
                "application/json");

            using var timeout = new CancellationTokenSource(Math.Max(1000, _timeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await HttpClient.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return TranslationResult.Failure(Kind, request.SourceLanguage, request.TargetLanguage, stopwatch.Elapsed, $"Microsoft Translator HTTP {(int)response.StatusCode}: {TrimSnippet(body)}");
            }

            var translated = ExtractTranslatedText(body);
            if (string.IsNullOrWhiteSpace(translated))
            {
                return TranslationResult.Failure(Kind, request.SourceLanguage, request.TargetLanguage, stopwatch.Elapsed, "Microsoft Translator returned empty text.");
            }

            return new TranslationResult(
                true,
                translated,
                Kind,
                request.SourceLanguage,
                request.TargetLanguage,
                stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return TranslationResult.Failure(Kind, request.SourceLanguage, request.TargetLanguage, stopwatch.Elapsed, ex.Message);
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        lock (_tokenGate)
        {
            if (!string.IsNullOrWhiteSpace(_token) && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
            {
                return _token;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.AuthEndpoint);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 GenshinChatTranslator/0.1");
        using var timeout = new CancellationTokenSource(Math.Max(1000, _timeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var response = await HttpClient.SendAsync(request, linked.Token).ConfigureAwait(false);
        var token = (await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false)).Trim();
        if (!response.IsSuccessStatusCode || string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException($"Failed to get Microsoft Translator token: HTTP {(int)response.StatusCode}.");
        }

        lock (_tokenGate)
        {
            _token = token;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(8);
        }

        return token;
    }

    private static string ExtractTranslatedText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var first = document.RootElement[0];
        if (!first.TryGetProperty("translations", out var translations) ||
            translations.ValueKind != JsonValueKind.Array ||
            translations.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        return translations[0].TryGetProperty("text", out var text)
            ? text.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static string TrimSnippet(string text)
    {
        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
