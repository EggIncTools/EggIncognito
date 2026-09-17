using System.Text.Json;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed record CarvedManifest(int V, string FileSha, int? ClientVersion, string Ei, string? Common,
    string? AppVersion, string? Build) {
    private static readonly JsonSerializerOptions Opts = JsonPresets.Web;

    public static bool LooksLikeManifest(byte[]? bytes) => bytes is { Length: > 0 } && bytes[0] == (byte)'{';

    public static CarvedManifest? TryParse(byte[]? bytes) {
        if (!LooksLikeManifest(bytes)) return null;
        try {
            var m = JsonSerializer.Deserialize<CarvedManifest>(bytes, Opts);
            if (m is null || string.IsNullOrEmpty(m.Ei) || string.IsNullOrEmpty(m.FileSha)) return null;
            return m;
        } catch {
            return null;
        }
    }
}
