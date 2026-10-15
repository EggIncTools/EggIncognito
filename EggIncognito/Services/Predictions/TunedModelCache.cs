namespace EggIncognito.Services.Predictions;

public abstract class TunedModelCache<T> {
    private readonly Lock _gate = new();
    private long _version = -1;
    private T? _value;

    public bool TryGet(long version, out T value) {
        lock (_gate) {
            if (_version == version && _value is not null) {
                value = _value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public void Set(long version, T value) {
        lock (_gate) {
            _version = version;
            _value = value;
        }
    }
}
