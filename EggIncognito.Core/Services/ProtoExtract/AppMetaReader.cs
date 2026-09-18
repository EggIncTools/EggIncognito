using System.Net;
using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class AppMetaReader {
    public static (string? AppVersion, string? Build) Read(byte[]? meta) {
        if (meta is not { Length: >= 4 }) return (null, null);
        if (meta[0] == 0x03 && meta[1] == 0x00)
            return (ApkVersionCode.ReadVersionName(meta), ApkVersionCode.ParseAxml(meta));
        string text = Encoding.UTF8.GetString(meta);
        return (PlistShortVersion(text), PlistBundleVersion(text));
    }

    public static string? PlistShortVersion(string plistXml) => PlistString(plistXml, "CFBundleShortVersionString");

    public static string? PlistBundleVersion(string plistXml) => PlistString(plistXml, "CFBundleVersion");

    private static string? PlistString(string plistXml, string key) {
        string keyTag = $"<key>{key}</key>";
        int ki = plistXml.IndexOf(keyTag, StringComparison.Ordinal);
        if (ki < 0) return null;
        int open = plistXml.IndexOf("<string>", ki + keyTag.Length, StringComparison.Ordinal);
        if (open < 0) return null;
        int start = open + "<string>".Length;
        int close = plistXml.IndexOf("</string>", start, StringComparison.Ordinal);
        if (close < 0) return null;
        string val = plistXml[start..close].Trim();
        return val.Length == 0 ? null : WebUtility.HtmlDecode(val);
    }
}
