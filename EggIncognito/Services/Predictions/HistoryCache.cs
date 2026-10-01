namespace EggIncognito.Services.Predictions;

public abstract class HistoryCache<TRow> {
    private readonly Lock _gate = new();
    private long _version = -1;
    private IReadOnlyList<TRow>? _rows;

    public bool TryGet(long version, out IReadOnlyList<TRow> rows) {
        lock (_gate) {
            if (_version == version && _rows is not null) {
                rows = _rows;
                return true;
            }
        }

        rows = [];
        return false;
    }

    public void Set(long version, IReadOnlyList<TRow> rows) {
        lock (_gate) {
            _version = version;
            _rows = rows;
        }
    }
}
