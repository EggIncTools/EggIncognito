using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Core.Services.ProtoExtract;

public enum ProtoRefSource { Registry, Session }

public sealed record ProtoRef(ProtoRefSource Source, string Platform, string Build, string? FileSha) {
    public string Format() => $"{Platform}_{Build}";
}

public sealed record WorkbenchRef(ProtoRef? A, ProtoRef? B, string? Mode);

public static class ProtoRefParser {
    public const string Separator = "...";
    public const string SessionPlatform = "file";

    public static readonly string[] Modes = ["text", "split", "unified", "struct", "meta"];

    public static readonly string[] Known = [Platforms.Ios, Platforms.Android];

    private static readonly WorkbenchRef Empty = new(null, null, null);

    public static WorkbenchRef Parse(string? hash) {
        if (string.IsNullOrWhiteSpace(hash)) return Empty;

        string body = hash.Trim();
        if (body.StartsWith('#')) body = body[1..];
        if (body.Length == 0) return Empty;

        string? mode = null;
        int slash = body.LastIndexOf('/');
        if (slash >= 0) {
            string candidate = body[(slash + 1)..];
            mode = Array.Find(Modes, m => m.Equals(candidate, StringComparison.OrdinalIgnoreCase));
            body = body[..slash];
        }

        int sep = body.IndexOf(Separator, StringComparison.Ordinal);
        string first = sep < 0 ? body : body[..sep];
        string second = sep < 0 ? "" : body[(sep + Separator.Length)..];
        int extra = second.IndexOf(Separator, StringComparison.Ordinal);
        if (extra >= 0) second = second[..extra];

        return new WorkbenchRef(ParsePart(first), ParsePart(second), mode);
    }

    public static string Format(WorkbenchRef value) {
        if (value.A is null) return "";

        string text = value.A.Format();
        if (value.B is not null) text += Separator + value.B.Format();
        if (!string.IsNullOrEmpty(value.Mode)) text += "/" + value.Mode;
        return text;
    }

    private static ProtoRef? ParsePart(string part) {
        int underscore = part.IndexOf('_');
        if (underscore <= 0 || underscore == part.Length - 1) return null;

        string platform = part[..underscore];
        string build = part[(underscore + 1)..];
        if (platform.Equals(SessionPlatform, StringComparison.OrdinalIgnoreCase))
            return new ProtoRef(ProtoRefSource.Session, SessionPlatform, build, build);

        return Known.Any(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase))
            ? new ProtoRef(ProtoRefSource.Registry, platform, build, null)
            : null;
    }
}
