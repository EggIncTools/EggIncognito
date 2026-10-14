using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core;
using EggIncognito.Core.Services;

namespace EggIncognito.Services.DataApi;

public sealed class ConfigSliceCache {
    private static readonly string[] Fields = [
        "items", "shells", "shellSets", "shellObjects", "shellGroups", "decorators"
    ];

    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;

    private readonly Lock _lock = new();
    private string? _cachedHash;
    private Dictionary<string, byte[]>? _cachedSlices;

    public async Task<DataPayload?> SliceAsync(EndpointStore store, string route, string field, CancellationToken ct) {
        string? text = await store.CurrentJsonAsync(route, ct);
        if (text is null) return null;

        string hash = Hashes.Sha256Hex(text);
        lock (_lock) {
            if (_cachedSlices is null || !string.Equals(_cachedHash, hash, StringComparison.Ordinal)) {
                _cachedSlices = Build(text, route);
                _cachedHash = hash;
            }

            return _cachedSlices is not null && _cachedSlices.TryGetValue(field, out byte[]? bytes)
                ? new DataPayload(bytes, "application/json")
                : null;
        }
    }

    private static Dictionary<string, byte[]>? Build(string text, string route) {
        JsonNode? root;
        try {
            root = JsonNode.Parse(text);
        } catch {
            return null;
        }

        if (root is not JsonObject rootObj || rootObj["dlcCatalog"] is not JsonObject dlc) return null;

        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string field in Fields) {
            if (dlc[field] is not JsonNode value) continue;
            var slice = new JsonObject {
                [field] = value.DeepClone(),
                ["provenance"] = new JsonObject {
                    ["source"] = route,
                    ["path"] = $"dlcCatalog.{field}"
                }
            };
            result[field] = JsonSerializer.SerializeToUtf8Bytes(slice, IndentedJson);
        }

        return result;
    }
}
