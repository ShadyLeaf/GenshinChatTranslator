using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenshinChatTranslator.App.Services;

namespace GenshinChatTranslator.App.Ocr.Engines;

internal sealed class OpenAiVisionOcrEngine : IChatOcrEngine
{
    private const string Prompt = "Recognize the chat text in the image. The text may be Simplified Chinese, English, Japanese, or mixed. Return only the recognized text. Do not translate. Do not explain. If no text is visible, return an empty string.";
    private static readonly HttpClient HttpClient = new();
    private readonly OpenAiVisionOcrOptions _options;

    public OpenAiVisionOcrEngine(OpenAiVisionOcrOptions options)
    {
        _options = options;
    }

    public OcrEngineKind Kind => OcrEngineKind.OpenAiVision;

    public IReadOnlySet<ChatLanguage> SupportedLanguages { get; } =
        new HashSet<ChatLanguage>
        {
            ChatLanguage.Auto,
            ChatLanguage.ChineseSimplified,
            ChatLanguage.English,
            ChatLanguage.Japanese,
        };

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                stopwatch.Stop();
                return OcrResult.Failure(Kind, stopwatch.Elapsed, "OpenAI vision OCR API key is empty. Set ocr.engines.openai_vision.api_key or OPENAI_API_KEY.");
            }

            var imageBytes = RgbFramePngWriter.Encode(request.PreparedImage);
            var payload = BuildPayload(Convert.ToBase64String(imageBytes));
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpRequest.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var timeout = new CancellationTokenSource(Math.Max(1000, _options.TimeoutMs));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var response = await HttpClient.SendAsync(httpRequest, linked.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return OcrResult.Failure(Kind, stopwatch.Elapsed, $"OpenAI vision OCR HTTP {(int)response.StatusCode}: {TrimSnippet(body)}");
            }

            var text = ExtractText(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                return OcrResult.Empty(Kind, stopwatch.Elapsed);
            }

            return new OcrResult(
                IsSuccess: true,
                Text: CleanModelText(text),
                Confidence: 0.90,
                DetectedLanguage: ChatLanguage.Auto,
                Engine: Kind,
                Duration: stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            return OcrResult.Failure(Kind, stopwatch.Elapsed, ex.Message);
        }
    }

    private string ResolveApiKey()
    {
        return !string.IsNullOrWhiteSpace(_options.ApiKey)
            ? _options.ApiKey.Trim()
            : Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
    }

    private string BuildPayload(string base64Png)
    {
        var payload = new
        {
            model = _options.Model,
            temperature = 0,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = Prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = $"data:image/png;base64,{base64Png}",
                            },
                        },
                    },
                },
            },
        };

        return JsonSerializer.Serialize(payload);
    }

    private static string ExtractText(string responseBody)
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

    private static string CleanModelText(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal) && cleaned.EndsWith("```", StringComparison.Ordinal))
        {
            cleaned = cleaned.Trim('`').Trim();
        }

        return cleaned.Trim().Trim('"', '\'', '“', '”');
    }

    private static string TrimSnippet(string text)
    {
        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }
}
