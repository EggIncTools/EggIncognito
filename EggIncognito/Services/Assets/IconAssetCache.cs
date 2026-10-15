namespace EggIncognito.Services.Assets;

public sealed class IconAssetCache(IConfiguration config) {
    private string? CacheRoot {
        get {
            string? dir = config["ShipAssets:OutputDir"];
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "icons");
        }
    }

    public bool Enabled => CacheRoot is not null;

    public byte[]? TryGet(string name) {
        string? path = PathFor(name);
        if (path is null || !File.Exists(path)) return null;
        try {
            return File.ReadAllBytes(path);
        } catch {
            return null;
        }
    }

    public async Task PutAsync(string name, byte[] bytes, CancellationToken ct) {
        string? path = PathFor(name);
        if (path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct);
    }

    public int ClearAll() {
        string? root = CacheRoot;
        if (root is null || !Directory.Exists(root)) return 0;
        string[] files = Directory.GetFiles(root, "*.png");
        foreach (string file in files) {
            try {
                File.Delete(file);
            } catch {
            }
        }

        return files.Length;
    }

    private string? PathFor(string name) {
        string? root = CacheRoot;
        return root is null || string.IsNullOrEmpty(name) ? null : Path.Combine(root, Safe(name) + ".png");
    }

    private static string Safe(string s) {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (char ch in s) {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
                buf[n++] = ch;
        }

        return n == 0 ? "_" : new string(buf[..n]);
    }
}
