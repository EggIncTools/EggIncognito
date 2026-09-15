namespace EggIncognito.Data.Services;

public sealed class BlobOffloadGate(bool enabled) {
    public const string SettingKey = "storage.blob_offload_enabled";

    public bool Enabled { get; } = enabled;
}
