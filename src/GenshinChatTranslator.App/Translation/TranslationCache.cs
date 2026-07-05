namespace GenshinChatTranslator.App.Translation;

internal sealed class TranslationCache
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _items = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recency = new();

    public TranslationCache(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool TryGet(string key, out TranslationResult result)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue(key, out var entry))
            {
                result = null!;
                return false;
            }

            _recency.Remove(entry.Node);
            _recency.AddLast(entry.Node);
            result = entry.Result;
            return true;
        }
    }

    public void Set(string key, TranslationResult result)
    {
        if (!result.IsSuccess)
        {
            return;
        }

        lock (_gate)
        {
            if (_items.TryGetValue(key, out var existing))
            {
                _recency.Remove(existing.Node);
                existing.Node.Value = key;
                _recency.AddLast(existing.Node);
                _items[key] = existing with { Result = result };
                return;
            }

            var node = _recency.AddLast(key);
            _items[key] = new CacheEntry(result, node);
            while (_items.Count > _capacity && _recency.First is not null)
            {
                var oldestKey = _recency.First.Value;
                _recency.RemoveFirst();
                _items.Remove(oldestKey);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _recency.Clear();
        }
    }

    private sealed record CacheEntry(
        TranslationResult Result,
        LinkedListNode<string> Node);
}
