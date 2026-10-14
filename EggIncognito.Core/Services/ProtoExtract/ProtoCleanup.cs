using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public static partial class ProtoCleanup {
    [GeneratedRegex(@"\.?\b(?:ei|aux)\.[\w.]+")]
    private static partial Regex QualifiedRe();

    private const string LegacyCommonProto =
        "syntax = \"proto2\";\n" +
        "\n" +
        "package aux;\n" +
        "\n" +
        "enum Platform {\n" +
        "    UNKNOWN_PLATFORM = 0;\n" +
        "    IOS = 1;\n" +
        "    DROID = 2;\n" +
        "}\n" +
        "\n" +
        "enum DeviceFormFactor {\n" +
        "    UNKNOWN_DEVICE = 0;\n" +
        "    PHONE = 1;\n" +
        "    TABLET = 2;\n" +
        "}\n" +
        "\n" +
        "enum AdNetwork {\n" +
        "    VUNGLE = 0;\n" +
        "    CHARTBOOST = 1;\n" +
        "    AD_COLONY = 2;\n" +
        "    HYPER_MX = 3;\n" +
        "    UNITY = 4;\n" +
        "    FACEBOOK = 5;\n" +
        "    APPLOVIN = 6;\n" +
        "}\n";

    public static string MergeLegacyCommon(string eiProto) => Clean(eiProto, LegacyCommonProto);

    public static string Clean(string eiProto, string commonProto) {
        var commonLines = UnifiedDiffWriter.SplitLines(commonProto, keepEnds: true).ToList();
        commonLines = [.. commonLines.Skip(3)];
        if (commonLines.Count > 0)
            commonLines[^1] = commonLines[^1].TrimEnd();

        var lines = UnifiedDiffWriter.SplitLines(eiProto, keepEnds: true).ToList();

        lines = [.. lines.Where(l => !l.TrimStart().StartsWith("import \"common.proto\";", StringComparison.Ordinal))];

        int packageIndex = lines.FindIndex(l => l.TrimStart().StartsWith("package ei;", StringComparison.Ordinal));
        if (packageIndex >= 0 && commonLines.Count > 0)
            lines.InsertRange(packageIndex + 1, commonLines);

        lines = [.. lines.Select(l => QualifiedRe().Replace(l, m => ProtoNames.StripQualifier(m.Value)))];

        return string.Concat(lines);
    }
}
