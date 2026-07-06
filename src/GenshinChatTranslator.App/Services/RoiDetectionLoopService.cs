using System.Diagnostics;
using System.Text;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Translation;

namespace GenshinChatTranslator.App.Services;

public sealed class RoiDetectionLoopService : IDisposable
{
    private const double SupportedAspectRatio = 16d / 9d;
    private const double SupportedAspectRatioTolerance = 0.01d;
    private readonly object _gate = new();
    private readonly string[] _titleKeywords;
    private readonly TimeSpan _nonChatInterval = TimeSpan.FromMilliseconds(200);
    private readonly TimeSpan _fastDetectionCompletedFlowInterval = TimeSpan.Zero;
    private readonly TimeSpan _standardDetectionCompletedFlowInterval = TimeSpan.FromMilliseconds(1000);
    private readonly WindowTracker _windowTracker = new();
    private readonly GdiFrameCaptureService _captureService = new();
    private readonly ChatUiGate _chatUiGate = new();
    private readonly PipelineLatencyAverager _latencyAverager = new();
    private readonly ChatRoiDetector _roiDetector;
    private readonly ChatOcrPipeline _ocrPipeline;
    private ChatTranslationPipeline _translationPipeline;

    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private int _generation;
    private bool _skipChatUiGate;
    private bool _fastDetection;
    private string? _lastTranslationSignature;
    private IReadOnlyList<ChatTranslationItem> _lastTranslationResults = Array.Empty<ChatTranslationItem>();
    private RoiDetectionLoopSnapshot _snapshot = new(
        IsRunning: false,
        Result: null,
        ErrorMessage: null,
        TargetMissing: false,
        TargetBackground: false,
        ChatInterfaceMissing: false,
        UnsupportedAspectWindow: null,
        LatencyAverages: null,
        UpdatedAt: DateTime.Now);

    public RoiDetectionLoopService(
        IEnumerable<string> titleKeywords,
        RoiDetectionConfig config,
        ChatOcrPipeline ocrPipeline,
        ChatTranslationPipeline translationPipeline)
    {
        _titleKeywords = titleKeywords.ToArray();
        _roiDetector = new ChatRoiDetector(config);
        _ocrPipeline = ocrPipeline;
        _translationPipeline = translationPipeline;
    }

    public RoiDetectionLoopSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Start(bool skipChatUiGate = false)
    {
        lock (_gate)
        {
            if (_cancellation is not null)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            var cancellationToken = _cancellation.Token;
            var generation = ++_generation;
            _skipChatUiGate = skipChatUiGate;
            _lastTranslationSignature = null;
            _lastTranslationResults = Array.Empty<ChatTranslationItem>();
            _latencyAverager.Clear();
            _snapshot = new RoiDetectionLoopSnapshot(
                IsRunning: true,
                Result: null,
                ErrorMessage: null,
                TargetMissing: false,
                TargetBackground: false,
                ChatInterfaceMissing: false,
                UnsupportedAspectWindow: null,
                LatencyAverages: null,
                UpdatedAt: DateTime.Now);
            _worker = Task.Run(() => RunAsync(generation, cancellationToken));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            cancellation = _cancellation;
            _cancellation = null;
            _worker = null;
            _generation++;
            _lastTranslationSignature = null;
            _lastTranslationResults = Array.Empty<ChatTranslationItem>();
            _latencyAverager.Clear();
            _snapshot = _snapshot with
            {
                IsRunning = false,
                ChatInterfaceMissing = false,
                UnsupportedAspectWindow = null,
                LatencyAverages = null,
                UpdatedAt = DateTime.Now,
            };
        }

        cancellation?.Cancel();
    }

    public void UpdateTranslationPipeline(ChatTranslationPipeline translationPipeline)
    {
        lock (_gate)
        {
            _translationPipeline = translationPipeline;
            _lastTranslationSignature = null;
            _lastTranslationResults = Array.Empty<ChatTranslationItem>();
            _latencyAverager.Clear();
        }
    }

    public void SetFastDetection(bool enabled)
    {
        lock (_gate)
        {
            _fastDetection = enabled;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var delay = await DetectOnceAsync(generation, cancellationToken).ConfigureAwait(false);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<TimeSpan> DetectOnceAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            var window = _windowTracker.FindTargetWindow(_titleKeywords);
            if (window is null)
            {
                Publish(new RoiDetectionLoopSnapshot(
                    IsRunning: true,
                    Result: null,
                    ErrorMessage: null,
                    TargetMissing: true,
                    TargetBackground: false,
                    ChatInterfaceMissing: false,
                    UnsupportedAspectWindow: null,
                    LatencyAverages: _latencyAverager.Current,
                    UpdatedAt: DateTime.Now), generation);
                _lastTranslationSignature = null;
                _lastTranslationResults = Array.Empty<ChatTranslationItem>();
                return _nonChatInterval;
            }

            if (!_windowTracker.IsForegroundWindow(window))
            {
                Publish(new RoiDetectionLoopSnapshot(
                    IsRunning: true,
                    Result: null,
                    ErrorMessage: null,
                    TargetMissing: false,
                    TargetBackground: true,
                    ChatInterfaceMissing: false,
                    UnsupportedAspectWindow: null,
                    LatencyAverages: _latencyAverager.Current,
                    UpdatedAt: DateTime.Now), generation);
                _lastTranslationSignature = null;
                _lastTranslationResults = Array.Empty<ChatTranslationItem>();
                return _nonChatInterval;
            }

            if (!_skipChatUiGate && !IsSupportedAspectRatio(window.ClientBox.Width, window.ClientBox.Height))
            {
                Publish(new RoiDetectionLoopSnapshot(
                    IsRunning: true,
                    Result: null,
                    ErrorMessage: null,
                    TargetMissing: false,
                    TargetBackground: false,
                    ChatInterfaceMissing: false,
                    UnsupportedAspectWindow: window,
                    LatencyAverages: _latencyAverager.Current,
                    UpdatedAt: DateTime.Now), generation);
                return _nonChatInterval;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var totalWatch = Stopwatch.StartNew();
            var captureWatch = Stopwatch.StartNew();
            var frame = _captureService.Capture(window);
            captureWatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            var chatGateWatch = Stopwatch.StartNew();
            var isChatInterface = true;
            if (!_skipChatUiGate)
            {
                var chatUiGateResult = _chatUiGate.Detect(frame);
                isChatInterface = chatUiGateResult.IsChatInterface;
            }
            chatGateWatch.Stop();
            if (!isChatInterface)
            {
                Publish(new RoiDetectionLoopSnapshot(
                    IsRunning: true,
                    Result: null,
                    ErrorMessage: null,
                    TargetMissing: false,
                    TargetBackground: false,
                    ChatInterfaceMissing: true,
                    UnsupportedAspectWindow: null,
                    LatencyAverages: _latencyAverager.Current,
                    UpdatedAt: DateTime.Now), generation);
                return _nonChatInterval;
            }

            var roiWatch = Stopwatch.StartNew();
            var (messageRoi, rois) = _roiDetector.Locate(frame);
            roiWatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ChatOcrItem> ocrResults = Array.Empty<ChatOcrItem>();
            IReadOnlyList<ChatTranslationItem> translationResults = Array.Empty<ChatTranslationItem>();
            var translationRequestSent = false;
            var ocrElapsedMs = 0d;
            var translationElapsedMs = 0d;
            if (rois.Count > 0)
            {
                if (_translationPipeline.CanTranslateSelectedEnginePerItem)
                {
                    var run = await RecognizeAndTranslateSingleItemsAsync(frame, rois, cancellationToken).ConfigureAwait(false);
                    ocrResults = run.OcrResults;
                    translationResults = run.TranslationResults;
                    translationRequestSent = run.RequestSent;
                    ocrElapsedMs = run.OcrMs;
                    translationElapsedMs = run.TranslationMs;
                }
                else
                {
                    var ocrWatch = Stopwatch.StartNew();
                    ocrResults = await _ocrPipeline.RecognizeAsync(frame, rois, cancellationToken).ConfigureAwait(false);
                    ocrWatch.Stop();
                    ocrElapsedMs = ocrWatch.Elapsed.TotalMilliseconds;

                    var translationWatch = Stopwatch.StartNew();
                    var translationRun = await TranslateOnlyWhenOcrTextChangedAsync(ocrResults, cancellationToken).ConfigureAwait(false);
                    translationWatch.Stop();
                    translationElapsedMs = translationWatch.Elapsed.TotalMilliseconds;
                    translationResults = translationRun.Results;
                    translationRequestSent = translationRun.RequestSent;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            totalWatch.Stop();
            var latencyAverages = translationRequestSent
                ? _latencyAverager.Add(new PipelineLatencySample(
                    totalWatch.Elapsed.TotalMilliseconds,
                    captureWatch.Elapsed.TotalMilliseconds,
                    chatGateWatch.Elapsed.TotalMilliseconds,
                    roiWatch.Elapsed.TotalMilliseconds,
                    ocrElapsedMs,
                    translationElapsedMs))
                : _latencyAverager.Current;

            Publish(new RoiDetectionLoopSnapshot(
                IsRunning: true,
                Result: new LiveDetectionResult(
                    window,
                    frame.Width,
                    frame.Height,
                    messageRoi,
                    rois,
                    ocrResults,
                    translationResults,
                    DateTime.Now),
                ErrorMessage: null,
                TargetMissing: false,
                TargetBackground: false,
                ChatInterfaceMissing: false,
                UnsupportedAspectWindow: null,
                LatencyAverages: latencyAverages,
                UpdatedAt: DateTime.Now), generation);
            return GetCompletedFlowInterval();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Publish(new RoiDetectionLoopSnapshot(
                IsRunning: true,
                Result: null,
                ErrorMessage: ex.Message,
                TargetMissing: false,
                TargetBackground: false,
                ChatInterfaceMissing: false,
                UnsupportedAspectWindow: null,
                LatencyAverages: _latencyAverager.Current,
                UpdatedAt: DateTime.Now), generation);
            return _nonChatInterval;
        }
    }

    private async Task<SingleItemPipelineRunResult> RecognizeAndTranslateSingleItemsAsync(
        RgbFrame frame,
        IReadOnlyList<ChatBubbleRoi> rois,
        CancellationToken cancellationToken)
    {
        var translationTasks = new List<Task<TimestampedTranslationBatch>>(rois.Count);
        var translationTasksGate = new object();
        var ocrStartTimestamp = Stopwatch.GetTimestamp();
        var ocrResults = await _ocrPipeline.RecognizeAsync(
            frame,
            rois,
            cancellationToken,
            itemRecognized: item =>
            {
                var task = TranslateSingleItemAndTimestampAsync(item, cancellationToken);
                lock (translationTasksGate)
                {
                    translationTasks.Add(task);
                }
            }).ConfigureAwait(false);
        var ocrEndTimestamp = Stopwatch.GetTimestamp();

        Task<TimestampedTranslationBatch>[] pendingTranslationTasks;
        lock (translationTasksGate)
        {
            pendingTranslationTasks = translationTasks.ToArray();
        }

        var timestampedBatches = pendingTranslationTasks.Length == 0
            ? Array.Empty<TimestampedTranslationBatch>()
            : await Task.WhenAll(pendingTranslationTasks).ConfigureAwait(false);
        var translationResults = timestampedBatches
            .SelectMany(batch => batch.Batch.Items)
            .OrderBy(item => item.Index)
            .ToArray();
        var requestSent = timestampedBatches.Any(batch => batch.Batch.RequestSent);
        var lastTranslationEndTimestamp = timestampedBatches.Length == 0
            ? ocrEndTimestamp
            : timestampedBatches.Max(batch => batch.CompletedTimestamp);
        var translationMs = requestSent
            ? Math.Max(0, ElapsedMilliseconds(ocrEndTimestamp, lastTranslationEndTimestamp))
            : 0;

        return new SingleItemPipelineRunResult(
            ocrResults,
            translationResults,
            requestSent,
            ElapsedMilliseconds(ocrStartTimestamp, ocrEndTimestamp),
            translationMs);
    }

    private async Task<TimestampedTranslationBatch> TranslateSingleItemAndTimestampAsync(
        ChatOcrItem item,
        CancellationToken cancellationToken)
    {
        var batch = await _translationPipeline.TranslateSingleWithStatsAsync(item, cancellationToken).ConfigureAwait(false);
        return new TimestampedTranslationBatch(batch, Stopwatch.GetTimestamp());
    }

    private async Task<TranslationRunResult> TranslateOnlyWhenOcrTextChangedAsync(
        IReadOnlyList<ChatOcrItem> ocrResults,
        CancellationToken cancellationToken)
    {
        var signature = BuildTranslationSignature(ocrResults);
        if (string.Equals(_lastTranslationSignature, signature, StringComparison.Ordinal))
        {
            return new TranslationRunResult(_lastTranslationResults, RequestSent: false);
        }

        var translationBatch = await _translationPipeline.TranslateWithStatsAsync(ocrResults, cancellationToken).ConfigureAwait(false);
        var translationResults = translationBatch.Items;
        _lastTranslationSignature = signature;
        _lastTranslationResults = translationResults;
        return new TranslationRunResult(translationResults, translationBatch.RequestSent);
    }

    private string BuildTranslationSignature(IReadOnlyList<ChatOcrItem> ocrResults)
    {
        var builder = new StringBuilder();
        builder
            .Append(_translationPipeline.SelectedEngineKind)
            .Append('|')
            .Append(_translationPipeline.TargetLanguage);

        foreach (var item in ocrResults.OrderBy(item => item.Index))
        {
            if (!item.Result.IsSuccess || string.IsNullOrWhiteSpace(item.Result.Text))
            {
                continue;
            }

            var text = item.Result.Text.ReplaceLineEndings("\n").Trim();
            builder
                .Append('\n')
                .Append(item.Index)
                .Append('|')
                .Append(item.Roi.Kind)
                .Append('|')
                .Append(item.Result.DetectedLanguage)
                .Append('|')
                .Append(text.Length)
                .Append('|')
                .Append(text);
        }

        return builder.ToString();
    }

    private void Publish(RoiDetectionLoopSnapshot snapshot, int generation)
    {
        lock (_gate)
        {
            if (_cancellation is null || generation != _generation)
            {
                return;
            }

            _snapshot = snapshot;
        }
    }

    private static double ElapsedMilliseconds(long startTimestamp, long endTimestamp)
    {
        return (endTimestamp - startTimestamp) * 1000d / Stopwatch.Frequency;
    }

    private TimeSpan GetCompletedFlowInterval()
    {
        lock (_gate)
        {
            return _fastDetection
                ? _fastDetectionCompletedFlowInterval
                : _standardDetectionCompletedFlowInterval;
        }
    }

    private static bool IsSupportedAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var actualAspectRatio = (double)width / height;
        var relativeError = Math.Abs(actualAspectRatio - SupportedAspectRatio) / SupportedAspectRatio;
        return relativeError <= SupportedAspectRatioTolerance;
    }

    private sealed record TranslationRunResult(
        IReadOnlyList<ChatTranslationItem> Results,
        bool RequestSent);

    private sealed record SingleItemPipelineRunResult(
        IReadOnlyList<ChatOcrItem> OcrResults,
        IReadOnlyList<ChatTranslationItem> TranslationResults,
        bool RequestSent,
        double OcrMs,
        double TranslationMs);

    private sealed record TimestampedTranslationBatch(
        ChatTranslationBatch Batch,
        long CompletedTimestamp);
}
