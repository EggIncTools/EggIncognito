using System.Globalization;

namespace EggIncognito.Core.Services.Devices;

public enum BridgePlaceholderKind {
    Input,
    Output
}

public static class BridgePlaceholders {
    private const string InPrefix = "@egi:in:";
    private const string OutPrefix = "@egi:out:";

    public static string In(string name) => InPrefix + name;

    public static string Out(string name) => OutPrefix + name;

    public static bool TryParse(string arg, out BridgePlaceholderKind kind, out string name) {
        if (arg.StartsWith(InPrefix, StringComparison.Ordinal)) {
            kind = BridgePlaceholderKind.Input;
            name = arg[InPrefix.Length..];
            return name.Length > 0;
        }

        if (arg.StartsWith(OutPrefix, StringComparison.Ordinal)) {
            kind = BridgePlaceholderKind.Output;
            name = arg[OutPrefix.Length..];
            return name.Length > 0;
        }

        kind = default;
        name = "";
        return false;
    }

    public static string Name(int index) => index.ToString(CultureInfo.InvariantCulture);
}
