namespace EggIncognito.Data.Services;

public static class BlobTables {
    public const string DeviceAssets = "device_assets";
    public const string BuildBlobs = "build_blobs";
    public const string StoredBinaries = "stored_binaries";
    public const string StoredApks = "stored_apks";
    public const string DeviceModules = "device_modules";

    public static readonly string[] All = [
        DeviceAssets, BuildBlobs, StoredBinaries, StoredApks, DeviceModules
    ];
}

public sealed class BlobBytes(BlobFileStore? files = null) {
    public static BlobBytes Inline { get; } = new();

    public BlobFileStore? Files => files;

    public async Task<byte[]?> StoreAsync(string table, string sha256, byte[] bytes, CancellationToken ct) {
        if (files is null || string.IsNullOrWhiteSpace(sha256)) return bytes;
        await files.WriteAsync(table, sha256, bytes, ct);
        return null;
    }

    public async Task<byte[]> ResolveAsync(string table, byte[]? inline, string sha256, CancellationToken ct) {
        if (inline is not null) return inline;
        if (files is null || string.IsNullOrWhiteSpace(sha256))
            throw new BlobFileMissingException(table, sha256, files?.PathFor(table, sha256) ?? "(no blob store)");

        return await files.ReadAsync(table, sha256, ct);
    }
}
