using System.Diagnostics;
using GenshinChatTranslator.App.Services;
using WeChatOcr;

namespace GenshinChatTranslator.App.Ocr.Engines;

internal sealed class WeChatOcrEngine : IChatOcrEngine
{
    private static readonly object InitGate = new();
    private static bool _baseDirectoryInitialized;
    private readonly WeChatOcrOptions _options;
    private readonly object _sessionsGate = new();
    private readonly Queue<WeChatOcrSession> _idleSessions = new();
    private readonly SemaphoreSlim _sessionLeaseGate;
    private bool _sessionsInitialized;
    private int _nextSessionId;

    public WeChatOcrEngine(WeChatOcrOptions options)
    {
        _options = options;
        _sessionLeaseGate = new SemaphoreSlim(Math.Max(1, options.SessionCount));
        EnsureBaseDirectory();
    }

    public OcrEngineKind Kind => OcrEngineKind.WeChat;

    public IReadOnlySet<ChatLanguage> SupportedLanguages { get; } =
        new HashSet<ChatLanguage>
        {
            ChatLanguage.Auto,
            ChatLanguage.ChineseSimplified,
            ChatLanguage.English,
        };

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        WeChatOcrSession? session = null;
        try
        {
            var imageBytes = RgbFramePngWriter.Encode(request.PreparedImage);
            session = await LeaseSessionAsync(cancellationToken).ConfigureAwait(false);
            var outcome = await session.RecognizeAsync(
                imageBytes,
                Math.Max(1000, _options.TimeoutMs),
                stopwatch,
                cancellationToken).ConfigureAwait(false);

            if (outcome.ReuseSession)
            {
                ReturnSession(session);
            }
            else
            {
                ReplaceSession(session);
            }

            session = null;
            return outcome.Result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            if (session is not null)
            {
                ReplaceSession(session);
                session = null;
            }

            return OcrResult.Failure(Kind, stopwatch.Elapsed, ex.Message);
        }
        finally
        {
            if (session is not null)
            {
                DiscardSession(session);
            }
        }
    }

    private async Task<WeChatOcrSession> LeaseSessionAsync(CancellationToken cancellationToken)
    {
        EnsureSessions();
        await _sessionLeaseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sessionsGate)
            {
                if (_idleSessions.Count > 0)
                {
                    return _idleSessions.Dequeue();
                }
            }

            return CreateSession();
        }
        catch
        {
            _sessionLeaseGate.Release();
            throw;
        }
    }

    private void ReturnSession(WeChatOcrSession session)
    {
        lock (_sessionsGate)
        {
            _idleSessions.Enqueue(session);
        }

        _sessionLeaseGate.Release();
    }

    private void ReplaceSession(WeChatOcrSession session)
    {
        session.Dispose();
        WeChatOcrSession? replacement = null;
        try
        {
            replacement = CreateSession();
        }
        catch
        {
        }

        if (replacement is not null)
        {
            lock (_sessionsGate)
            {
                _idleSessions.Enqueue(replacement);
            }
        }

        _sessionLeaseGate.Release();
    }

    private void DiscardSession(WeChatOcrSession session)
    {
        session.Dispose();
        _sessionLeaseGate.Release();
    }

    private void EnsureSessions()
    {
        if (_sessionsInitialized)
        {
            return;
        }

        lock (_sessionsGate)
        {
            if (_sessionsInitialized)
            {
                return;
            }

            try
            {
                for (var index = 0; index < Math.Max(1, _options.SessionCount); index++)
                {
                    _idleSessions.Enqueue(CreateSession());
                }

                _sessionsInitialized = true;
            }
            catch
            {
                while (_idleSessions.Count > 0)
                {
                    _idleSessions.Dequeue().Dispose();
                }

                throw;
            }
        }
    }

    private WeChatOcrSession CreateSession()
    {
        var sessionId = Interlocked.Increment(ref _nextSessionId);
        return new WeChatOcrSession(sessionId, new ImageOcr());
    }

    private static void EnsureBaseDirectory()
    {
        if (_baseDirectoryInitialized)
        {
            return;
        }

        lock (InitGate)
        {
            if (_baseDirectoryInitialized)
            {
                return;
            }

            DataLocation.SetBaseDirectory(AppContext.BaseDirectory);
            _baseDirectoryInitialized = true;
        }
    }

    private static OcrResult BuildResult(Stopwatch stopwatch, WeChatOcr.WeChatOcrResult? result)
    {
        var list = result?.OcrResult?.SingleResult;
        if (list is null)
        {
            return OcrResult.Empty(OcrEngineKind.WeChat, stopwatch.Elapsed);
        }

        var lines = list
            .Where(item => !string.IsNullOrWhiteSpace(item?.SingleStrUtf8))
            .Select(item => item!.SingleStrUtf8?.Trim() ?? string.Empty)
            .ToArray();
        var text = string.Join(Environment.NewLine, lines);
        if (string.IsNullOrWhiteSpace(text))
        {
            return OcrResult.Empty(OcrEngineKind.WeChat, stopwatch.Elapsed);
        }

        return new OcrResult(
            IsSuccess: true,
            Text: text,
            Confidence: 0.70,
            DetectedLanguage: ChatLanguage.Auto,
            Engine: OcrEngineKind.WeChat,
            Duration: stopwatch.Elapsed);
    }

    private readonly record struct SessionOcrOutcome(OcrResult Result, bool ReuseSession);

    private sealed class WeChatOcrSession : IDisposable
    {
        private readonly ImageOcr _ocr;

        public WeChatOcrSession(int id, ImageOcr ocr)
        {
            Id = id;
            _ocr = ocr;
        }

        public int Id { get; }

        public async Task<SessionOcrOutcome> RecognizeAsync(
            byte[] imageBytes,
            int timeoutMs,
            Stopwatch stopwatch,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<OcrResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ocr.Run(imageBytes, (_, result) =>
            {
                try
                {
                    tcs.TrySetResult(BuildResult(stopwatch, result));
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(OcrResult.Failure(OcrEngineKind.WeChat, stopwatch.Elapsed, $"WeChat OCR session {Id}: {ex.Message}"));
                }
            });

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            stopwatch.Stop();
            if (completedTask != tcs.Task)
            {
                return new SessionOcrOutcome(
                    OcrResult.Failure(OcrEngineKind.WeChat, stopwatch.Elapsed, $"WeChat OCR session {Id} timed out after {timeoutMs} ms."),
                    ReuseSession: false);
            }

            return new SessionOcrOutcome(await tcs.Task.ConfigureAwait(false), ReuseSession: true);
        }

        public void Dispose()
        {
            _ocr.Dispose();
        }
    }
}
