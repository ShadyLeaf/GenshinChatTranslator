using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Services;

public sealed class PipelineLatencyAverager
{
    private const int WindowSize = 8;
    private readonly Queue<double> _endToEndSamples = new();
    private readonly Queue<double> _captureSamples = new();
    private readonly Queue<double> _chatGateSamples = new();
    private readonly Queue<double> _roiSamples = new();
    private readonly Queue<double> _ocrSamples = new();
    private readonly Queue<double> _translationSamples = new();

    public PipelineLatencyAverages? Current => BuildAverage();

    public PipelineLatencyAverages Add(PipelineLatencySample sample)
    {
        AddSample(_endToEndSamples, sample.EndToEndMs);
        AddSampleIfPresent(_captureSamples, sample.CaptureMs);
        AddSampleIfPresent(_chatGateSamples, sample.ChatGateMs);
        AddSampleIfPresent(_roiSamples, sample.RoiMs);
        AddSampleIfPresent(_ocrSamples, sample.OcrMs);
        AddSampleIfPresent(_translationSamples, sample.TranslationMs);

        return BuildAverage() ?? new PipelineLatencyAverages(0, WindowSize, 0, null, null, null, null, null);
    }

    public void Clear()
    {
        _endToEndSamples.Clear();
        _captureSamples.Clear();
        _chatGateSamples.Clear();
        _roiSamples.Clear();
        _ocrSamples.Clear();
        _translationSamples.Clear();
    }

    private PipelineLatencyAverages? BuildAverage()
    {
        if (_endToEndSamples.Count == 0)
        {
            return null;
        }

        return new PipelineLatencyAverages(
            _endToEndSamples.Count,
            WindowSize,
            _endToEndSamples.Average(),
            AverageStage(_captureSamples),
            AverageStage(_chatGateSamples),
            AverageStage(_roiSamples),
            AverageStage(_ocrSamples),
            AverageStage(_translationSamples));
    }

    private static void AddSampleIfPresent(Queue<double> samples, double? value)
    {
        if (value.HasValue)
        {
            AddSample(samples, value.Value);
        }
    }

    private static void AddSample(Queue<double> samples, double value)
    {
        samples.Enqueue(value);
        while (samples.Count > WindowSize)
        {
            samples.Dequeue();
        }
    }

    private static double? AverageStage(Queue<double> samples)
    {
        return samples.Count == 0 ? null : samples.Average();
    }
}
