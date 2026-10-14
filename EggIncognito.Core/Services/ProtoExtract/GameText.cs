using System.Text;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class GameText {
    public static string Clean(string? raw) {
        if (raw is null) return "";
        var sb = new StringBuilder(raw.Length);
        for (int i = 0; i < raw.Length; i++) {
            if (raw[i] == '\x1b') {
                i++;
                continue;
            }

            sb.Append(raw[i]);
        }

        return sb.ToString();
    }

    public static bool HasMarkup(string? raw) => raw is not null && raw.Contains('\x1b');
}
