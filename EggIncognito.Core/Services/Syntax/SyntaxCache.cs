namespace EggIncognito.Core.Services.Syntax;

public sealed class SyntaxCache(int maxEntries = SyntaxCache.DefaultMaxEntries, long maxChars = SyntaxCache.DefaultMaxChars) {
    public const int DefaultMaxEntries = 64;
    public const long DefaultMaxChars = 16_000_000;

    private readonly Lock _gate = new();
#pragma warning disable IDE0028
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _index = new(StringComparer.Ordinal);
#pragma warning restore IDE0028
    private readonly LinkedList<CacheEntry> _order = new();
    private readonly int _maxEntries = Math.Max(1, maxEntries);
    private readonly long _maxChars = Math.Max(1, maxChars);
    private long _chars;

    public static SyntaxCache Shared { get; } = new();

    public int Count {
        get {
            lock (_gate) {
                return _index.Count;
            }
        }
    }

    public long Chars {
        get {
            lock (_gate) {
                return _chars;
            }
        }
    }

    public HighlightedText Get(string? text, ISyntaxTokenizer tokenizer) {
        string body = text ?? "";
        string key = KeyFor(tokenizer.Id, body);
        lock (_gate) {
            if (_index.TryGetValue(key, out var hit)) {
                _order.Remove(hit);
                _order.AddFirst(hit);
                return hit.Value.Value;
            }
        }

        var built = new HighlightedText(body, tokenizer);
        lock (_gate) {
            if (_index.TryGetValue(key, out var raced)) {
                _order.Remove(raced);
                _order.AddFirst(raced);
                return raced.Value.Value;
            }

            var node = _order.AddFirst(new CacheEntry(key, built));
            _index[key] = node;
            _chars += built.CharCount;
            Trim();
            return built;
        }
    }

    public void Clear() {
        lock (_gate) {
            _index.Clear();
            _order.Clear();
            _chars = 0;
        }
    }

    public static string KeyFor(string language, string text) => language + ":" + Hashes.Sha256Hex(text);

    private void Trim() {
        while (_order.Count > 1 && (_index.Count > _maxEntries || _chars > _maxChars)) {
            var last = _order.Last;
            if (last is null) return;
            _order.RemoveLast();
            _index.Remove(last.Value.Key);
            _chars -= last.Value.Value.CharCount;
            if (_chars < 0) _chars = 0;
        }
    }

    private sealed record CacheEntry(string Key, HighlightedText Value);
}
