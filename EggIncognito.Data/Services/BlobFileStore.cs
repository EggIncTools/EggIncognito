using System.Security.Cryptography;

namespace EggIncognito.Data.Services;

public sealed class BlobFileMissingException(string table, string sha256, string path)
    : IOException($"blob file missing for {table} sha {sha256}: expected at {path}") {
    public string Table { get; } = table;

    public string Sha256 { get; } = sha256;

    public string Path { get; } = path;
}

public sealed class BlobFileStore(string root) {
    public const string DirectoryName = "blobs";

    public string Root { get; } = root;

    public static BlobFileStore For(string contentRoot) =>
        new(Path.Combine(contentRoot, DirectoryName));

    public string PathFor(string table, string sha256) {
        string key = sha256.ToLowerInvariant();
        return Path.Combine(Root, table, Shard(key), key);
    }

    public bool Exists(string table, string sha256) => File.Exists(PathFor(table, sha256));

    public async Task<byte[]> ReadAsync(string table, string sha256, CancellationToken ct) {
        string path = PathFor(table, sha256);
        if (!File.Exists(path)) throw new BlobFileMissingException(table, sha256, path);
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task WriteAsync(string table, string sha256, byte[] bytes, CancellationToken ct) {
        string path = PathFor(table, sha256);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) return;

        string tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try {
            await using (var stream = new FileStream(
                tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous)) {
                await stream.WriteAsync(bytes, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }

            File.Move(tmp, path, false);
        } catch (IOException) when (File.Exists(path)) {
        } finally {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    public async Task<bool> VerifyAsync(string table, string sha256, CancellationToken ct) {
        string path = PathFor(table, sha256);
        if (!File.Exists(path)) return false;

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous);
        byte[] actual = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(actual).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string Shard(string sha256) => sha256.Length >= 2 ? sha256[..2] : "00";
}
