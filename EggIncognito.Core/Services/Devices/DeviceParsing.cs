using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EggIncognito.Core.Services.Devices;

public static partial class DeviceParsing {
    [GeneratedRegex(@"versionName=([^\s]+)")]
    private static partial Regex VersionNameRe();

    [GeneratedRegex(@"versionCode=(\d+)")]
    private static partial Regex VersionCodeRe();

    public static (string? AppVersion, string? Build) AndroidVersion(string dumpsys) {
        var name = VersionNameRe().Match(dumpsys);
        var code = VersionCodeRe().Match(dumpsys);
        return (name.Success ? name.Groups[1].Value : null,
            code.Success ? code.Groups[1].Value : null);
    }

    public static IReadOnlyList<string> ApkPaths(string pmPathOutput) {
        return [.. pmPathOutput.Split('\n')
            .Select(raw => raw.Trim())
            .Where(line => line.StartsWith("package:", StringComparison.Ordinal))
            .Select(line => line["package:".Length..].Trim())];
    }

    public static string? SelectArmSplit(string pmPathOutput) {
        var paths = ApkPaths(pmPathOutput);
        foreach (string p in paths) {
            if (p.Contains("arm64"))
                return p;
        }

        foreach (string p in paths) {
            if (p.Contains("arm"))
                return p;
        }

        return null;
    }

    public static IReadOnlyList<string> SelectConfigSplits(string pmPathOutput) {
        return [.. ApkPaths(pmPathOutput)
            .Where(p => p[(p.LastIndexOf('/') + 1)..]
                .StartsWith("split_config.", StringComparison.OrdinalIgnoreCase))];
    }

    public static string SplitNameFromPath(string apkPath) {
        string name = apkPath[(apkPath.LastIndexOf('/') + 1)..];
        int dot = name.LastIndexOf('.');
        if (dot > 0 && name[(dot + 1)..].Equals("apk", StringComparison.OrdinalIgnoreCase)) name = name[..dot];
        return name.StartsWith("split_", StringComparison.OrdinalIgnoreCase) ? name["split_".Length..] : name;
    }

    public static string? SelectBaseSplit(string pmPathOutput) {
        string? only = null;
        int count = 0;
        foreach (string p in ApkPaths(pmPathOutput)) {
            if (p.EndsWith("/base.apk", StringComparison.OrdinalIgnoreCase)) return p;
            only = p;
            count++;
        }

        return count == 1 ? only : null;
    }

    public static string? IosAppVersion(string output, string bundleId) => IosVersion(output, bundleId).AppVersion;

    public static (string? AppVersion, string? Build) IosVersion(string output, string bundleId) {
        var fromPlist = IosFromPlist(output, bundleId);
        if (fromPlist.AppVersion is not null) return fromPlist;
        string? csv = IosFromCsv(output, bundleId);
        return (csv, null);
    }

    private static (string? AppVersion, string? Build) IosFromPlist(string xml, string bundleId) {
        XDocument doc;
        try {
            doc = XDocument.Parse(xml);
        } catch {
            return (null, null);
        }

        foreach (var dict in doc.Descendants("dict")) {
            if (PlistString(dict, "CFBundleIdentifier") == bundleId)
                return (PlistString(dict, "CFBundleShortVersionString"), PlistString(dict, "CFBundleVersion"));
        }

        return (null, null);
    }

    private static string? PlistString(XElement dict, string key) {
        var nodes = dict.Elements().ToList();
        for (int i = 0; i < nodes.Count - 1; i++) {
            if (nodes[i].Name == "key" && nodes[i].Value == key && nodes[i + 1].Name == "string")
                return nodes[i + 1].Value;
        }

        return null;
    }

    private static string? IosFromCsv(string output, string bundleId) {
        foreach (string raw in output.Split('\n')) {
            string line = raw.Trim();
            int comma = line.IndexOf(',');
            if (comma < 0 || line[..comma].Trim() != bundleId) continue;
            int q1 = line.IndexOf('"', comma);
            if (q1 < 0) continue;
            int q2 = line.IndexOf('"', q1 + 1);
            if (q2 < 0) continue;
            return line[(q1 + 1)..q2];
        }

        return null;
    }

    public static string TrimNote(string s) => s.Trim() is { Length: > 200 } t ? t[..200] : s.Trim();

    public static int CompareVersions(string? a, string? b) {
        string[] pa = (a ?? "").Split('.');
        string[] pb = (b ?? "").Split('.');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++) {
            int x = i < pa.Length && int.TryParse(pa[i], out int xi) ? xi : 0;
            int y = i < pb.Length && int.TryParse(pb[i], out int yi) ? yi : 0;
            if (x != y) return x.CompareTo(y);
        }

        return 0;
    }
}
