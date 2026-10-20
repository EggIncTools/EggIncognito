using System.Collections.Frozen;
using System.Text.Json;

namespace EggIncognito.Services.Assets;

public static class EventPalette {
    private static Dictionary<string, string> _iconStems = [with(StringComparer.Ordinal)];

    public static void LoadFromJson(string? docJson) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(docJson)) {
            try {
                using var doc = JsonDocument.Parse(docJson);
                if (doc.RootElement.TryGetProperty("eventIcons", out var rows) && rows.ValueKind == JsonValueKind.Array) {
                    foreach (var row in rows.EnumerateArray()) {
                        string? type = row.TryGetProperty("eventType", out var t) ? t.GetString() : null;
                        string? stem = row.TryGetProperty("iconStem", out var st) ? st.GetString() : null;
                        if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(stem)) map[type] = stem;
                    }
                }
            } catch (JsonException) {
                map.Clear();
            }
        }

        _iconStems = map;
    }

    public static IEnumerable<string> KnownTypes => _iconStems.Keys;

    public static string? IconStem(string? eventType) =>
        string.IsNullOrEmpty(eventType) ? null : _iconStems.GetValueOrDefault(eventType.Replace('_', '-'));

    public const string Fallback = "#9ca3af";
    public const string CcGradientFrom = "#f5a709";
    public const string CcGradientTo = "#900fb1";

    private static readonly FrozenDictionary<string, string> ByType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        ["epic-research-sale"] = "#ef4444",
        ["piggy-boost"] = "#f97316",
        ["piggy-cap-boost"] = "#f59e0b",
        ["prestige-boost"] = "#f59e0b",
        ["earnings-boost"] = "#84cc16",
        ["gift-boost"] = "#10b981",
        ["drone-boost"] = "#10b981",
        ["research-sale"] = "#14b8a6",
        ["hab-sale"] = "#06b6d4",
        ["vehicle-sale"] = "#0ea5e9",
        ["boost-sale"] = "#3b82f6",
        ["boost-duration"] = "#6366f1",
        ["crafting-sale"] = "#8b5cf6",
        ["mission-fuel"] = "#8b5cf6",
        ["mission-capacity"] = "#d946ef",
        ["mission-duration"] = "#ec4899",
        ["shell-sale"] = "#f43f5e"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public const string BoostsGroup = "Boosts";
    public const string SalesGroup = "Sales";

    public static readonly string[] Groups = [BoostsGroup, SalesGroup];

    public static string GroupOf(string? eventType) =>
        eventType is not null && eventType.Contains("sale", StringComparison.OrdinalIgnoreCase) ? SalesGroup : BoostsGroup;

    public static string ColorFor(string? eventType) =>
        string.IsNullOrEmpty(eventType)
            ? Fallback
            : ByType.GetValueOrDefault(eventType.Replace('_', '-'), Fallback);

    public static string? IconUrl(string? eventType, bool ultra = false) =>
        string.IsNullOrEmpty(eventType) ? null : EggIcons.Asset("event-icon", eventType, ultra ? "&cc=1" : null);

    public static string BarStyle(string? eventType, bool ultra) =>
        ultra
            ? $"--evt-color:{CcGradientTo};--evt-from:{CcGradientFrom};--evt-to:{CcGradientTo};"
            : $"--evt-color:{ColorFor(eventType)};--evt-from:{ColorFor(eventType)};--evt-to:{ColorFor(eventType)};";
}
