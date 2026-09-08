using System.Diagnostics;

namespace EggIncognito.Services;

public sealed class TtlSnapshot<T>(TimeSpan ttl, Func<T> compute) where T : class {
    private readonly Lock _gate = new();
    private T? _value;
    private long _at;

    public T Get() {
        lock (_gate) {
            if (_value is not null && Stopwatch.GetElapsedTime(_at) < ttl) return _value;
            _value = compute();
            _at = Stopwatch.GetTimestamp();
            return _value;
        }
    }
}
