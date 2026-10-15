namespace EggIncognito.Services.Predictions;

public abstract class TunedModelCache<T> {
    private readonly Lock _gate = new();
    private long _version = -1;
    private Task<T>? _value;

    public bool TryGet(long version, out T value) {
        lock (_gate) {
            if (_version == version && _value is { IsCompletedSuccessfully: true } done) {
                value = done.Result;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Set(long version, T value) {
        lock (_gate) {
            _version = version;
            _value = Task.FromResult(value);
        }
    }

    public bool Update(long version, Func<T, T> mutate) {
        lock (_gate) {
            if (_version != version || _value is not { IsCompletedSuccessfully: true } done) return false;
            _value = Task.FromResult(mutate(done.Result));
            return true;
        }
    }
}
