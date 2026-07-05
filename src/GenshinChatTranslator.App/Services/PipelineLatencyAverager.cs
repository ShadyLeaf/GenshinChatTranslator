using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Services;

public sealed class PipelineLatencyAverager
{
    private const int WindowSize = 8;
    private readonly Queue<PipelineLatencySample> _samples = new();

    public PipelineLatencyAverages? Current => BuildAverage();

    public PipelineLatencyAverages Add(PipelineLatencySample sample)
    {
        _samples.Enqueue(sample);
        while (_samples.Count > WindowSize)
        {
            _samples.Dequeue();
        }

        return BuildAverage() ?? new PipelineLatencyAverages(0, WindowSize, 0, 0, 0, 0, 0, 0);
    }

    public void Clear()
    {
        _samples.Clear();
    }

    private PipelineLatencyAverages? BuildAverage()
    {
        if (_samples.Count == 0)
        {
            return null;
        }

        return new PipelineLatencyAverages(
            _samples.Count,
            WindowSize,
            _samples.Average(sample => sample.EndToEndMs),
            _samples.Average(sample => sample.CaptureMs),
            _samples.Average(sample => sample.ChatGateMs),
            _samples.Average(sample => sample.RoiMs),
            _samples.Average(sample => sample.OcrMs),
            _samples.Average(sample => sample.TranslationMs));
    }
}
