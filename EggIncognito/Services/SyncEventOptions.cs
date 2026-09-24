namespace EggIncognito.Services;

public sealed record SyncEventOptions {
    public string EventSecret { get; init; } = "";
    public string ApkFetchRoot { get; init; } = "";
}
