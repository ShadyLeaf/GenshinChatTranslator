namespace GenshinChatTranslator.App.Ocr;

internal sealed class OcrResultCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, OcrResult> _results = new(StringComparer.Ordinal);

    public bool TryGet(string imageHash, out OcrResult result)
    {
        lock (_gate)
        {
            return _results.TryGetValue(imageHash, out result!);
        }
    }

    public void Set(string imageHash, OcrResult result)
    {
        lock (_gate)
        {
            _results[imageHash] = result;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _results.Clear();
        }
    }
}
