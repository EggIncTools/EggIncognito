using System.Collections.Frozen;
using System.Globalization;
using EggSharp.GameData;

namespace EggIncognito.Services.DataApi;

public static class DimensionNames {
    private static readonly FrozenDictionary<int, string> ByCode =
        ColleggtibleCatalog.DimensionCodes.ToFrozenDictionary(kv => kv.Value, kv => kv.Key);

    private static readonly FrozenDictionary<string, int> ByName =
        ColleggtibleCatalog.DimensionCodes.ToFrozenDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    public static string Of(Ei.GameModifier.Types.GameDimension dimension) => Of((int)dimension);

    public static string Of(int code) =>
        ByCode.TryGetValue(code, out string? name) ? name : code.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string name, out Ei.GameModifier.Types.GameDimension dimension) {
        if (ByName.TryGetValue(name, out int code)) {
            dimension = (Ei.GameModifier.Types.GameDimension)code;
            return true;
        }

        if (Enum.TryParse(name, true, out dimension) && Enum.IsDefined(dimension)) return true;
        dimension = default;
        return false;
    }
}
