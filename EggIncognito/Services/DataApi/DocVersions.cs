using System.Text.Json;

namespace EggIncognito.Services.DataApi;

public static class DocVersions {
    public static string? BinaryVersion(JsonElement doc) {
        if (doc.ValueKind != JsonValueKind.Object) return null;
        if (!doc.TryGetProperty("binaryVersion", out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    public static string? BinaryVersion(string? json) {
        if (string.IsNullOrEmpty(json)) return null;
        try {
            using var parsed = JsonDocument.Parse(json);
            return BinaryVersion(parsed.RootElement);
        } catch (JsonException) {
            return null;
        }
    }
}
