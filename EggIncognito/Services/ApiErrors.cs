using System.Text.Json;

namespace EggIncognito.Services;

public static class ApiErrors {
    private const int MaxLength = 500;

    public static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct = default) {
        var fallback = $"HTTP {(int)response.StatusCode}";
        string body;
        try {
            body = await response.Content.ReadAsStringAsync(ct);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            return fallback;
        }

        if (string.IsNullOrWhiteSpace(body)) return fallback;
        var trimmed = body.Trim();

        if (trimmed.StartsWith('{')) {
            try {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object) {
                    foreach (var key in new[] { "error", "message" }) {
                        if (root.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String) {
                            var text = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(text)) return Clamp(text.Trim());
                        }
                    }
                }
            } catch (JsonException) {
            }
        }

        return Clamp(trimmed);
    }

    private static string Clamp(string text) => text.Length <= MaxLength ? text : text[..MaxLength];
}
