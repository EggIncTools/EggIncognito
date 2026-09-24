namespace EggIncognito.GameData;

public abstract class GameDataCatalog<TEntry, TKey>(IReadOnlyList<TEntry> entries, string version,
    IReadOnlyDictionary<string, ProvenanceSource> provenance, Func<TEntry, TKey> keyOf,
    IEqualityComparer<TKey>? comparer = null) where TEntry : class where TKey : notnull {
    private readonly Dictionary<TKey, TEntry> _byKey = entries.ToDictionary(keyOf, comparer);

    public IReadOnlyDictionary<string, ProvenanceSource> Provenance { get; } = provenance;

    protected IReadOnlyList<TEntry> Entries { get; } = entries;

    protected string Version { get; } = version;

    protected TEntry? FindByKey(TKey key) => _byKey.GetValueOrDefault(key);

    protected bool ContainsKey(TKey key) => _byKey.ContainsKey(key);
}
