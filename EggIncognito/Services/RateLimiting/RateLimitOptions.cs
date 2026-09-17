using System.Text.Json;

namespace EggIncognito.Services.RateLimiting;

public sealed record RateLimit(int PermitLimit, int WindowSeconds, int SegmentsPerWindow);

public sealed record RateLimitOptions(
    bool Enabled,
    IReadOnlyDictionary<string, RateLimit> Tiers,
    IReadOnlyDictionary<string, RateLimit> Policies) {
    public static RateLimitOptions Defaults() => new(
        true,
        new Dictionary<string, RateLimit> {
            ["Anon"] = new(30, 60, 6),
            ["Viewer"] = new(120, 60, 6),
            ["Contributor"] = new(600, 60, 6),
            ["Supporter"] = new(1200, 60, 6),
            ["Keyed"] = new(600, 60, 6)
        },
        new Dictionary<string, RateLimit> {
            ["Egress"] = new(10, 60, 6),
            ["Write"] = new(60, 60, 6),
            ["Read"] = new(120, 60, 6),

            ["Fetch"] = new(300, 60, 6),
            ["Data"] = new(600, 60, 6),
            ["DataAnon"] = new(30, 60, 6)
        });

    public static RateLimitOptions Bind(IConfiguration config) {
        var d = Defaults();
        var section = config.GetSection("RateLimiting");
        bool enabled = section.GetValue("Enabled", d.Enabled);
        var tiers = MergeGroup(section.GetSection("Tiers"), d.Tiers);
        var policies = MergeGroup(section.GetSection("Policies"), d.Policies);
        return new RateLimitOptions(
            enabled,
            OverlayJson(section["Tiers"], tiers),
            OverlayJson(section["Policies"], policies));
    }

    private static Dictionary<string, RateLimit> OverlayJson(
        string? json, IReadOnlyDictionary<string, RateLimit> current) {
        var result = new Dictionary<string, RateLimit>(current, StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var bucket in doc.RootElement.EnumerateObject()) {
                if (bucket.Value.ValueKind != JsonValueKind.Object) continue;
                var fallback = result.GetValueOrDefault(bucket.Name, new RateLimit(60, 60, 6));
                result[bucket.Name] = new RateLimit(
                    Int(bucket.Value, "PermitLimit", fallback.PermitLimit),
                    Int(bucket.Value, "WindowSeconds", fallback.WindowSeconds),
                    Int(bucket.Value, "SegmentsPerWindow", fallback.SegmentsPerWindow));
            }
        } catch (JsonException) {
            return new Dictionary<string, RateLimit>(current, StringComparer.Ordinal);
        }

        return result;
    }

    private static int Int(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out int parsed)
            ? parsed
            : fallback;

    private static Dictionary<string, RateLimit> MergeGroup(
        IConfigurationSection group, IReadOnlyDictionary<string, RateLimit> defaults) {
        var result = new Dictionary<string, RateLimit>(defaults);
        foreach (string key in defaults.Keys) {
            var s = group.GetSection(key);
            if (!s.Exists()) continue;
            var d = defaults[key];
            result[key] = new RateLimit(
                s.GetValue("PermitLimit", d.PermitLimit),
                s.GetValue("WindowSeconds", d.WindowSeconds),
                s.GetValue("SegmentsPerWindow", d.SegmentsPerWindow));
        }

        return result;
    }
}
