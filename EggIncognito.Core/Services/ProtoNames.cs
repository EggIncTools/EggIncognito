namespace EggIncognito.Core.Services;

public static class ProtoNames {
    public static string StripQualifier(string type) {
        var name = type.TrimStart('.');
        if (name.StartsWith("ei.", StringComparison.Ordinal)) name = name[3..];
        else if (name.StartsWith("aux.", StringComparison.Ordinal)) name = name[4..];
        var segments = name.Split('.');
        if (segments.Length == 1) return name;
        var kept = segments.Where(s => s.Length > 0 && char.IsUpper(s[0])).ToArray();
        return kept.Length == 0 ? segments[^1] : string.Join('.', kept);
    }

    public static string Short(string type) {
        var idx = type.LastIndexOf('.');
        return idx < 0 ? type : type[(idx + 1)..];
    }
}
