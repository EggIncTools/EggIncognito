using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services;

namespace EggIncognito.Services.DataApi;

public sealed class ConfigSliceCache {
    private static readonly string[] Fields = [
        "items", "shells", "shellSets", "shellObjects", "shellGroups", "decorators"
    ];

    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;

    private readonly Lock _lock = new();
    private string? _cachedPath;
    private DateTime _cachedStamp;
    private Dictionary<string, byte[]>? _cachedSlices;

    public DataPayload? Slice(IServiceProvider services, string route, string field) {
        string path = DataCatalog.FixturePath(services, route);
        lock (_lock) {
            if (!File.Exists(path)) {
                _cachedPath = null;
                _cachedSlices = null;
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (_cachedSlices is null || !string.Equals(_cachedPath, path, StringComparison.Ordinal) ||
                _cachedStamp != stamp) {
                _cachedSlices = Build(path, route);
                _cachedPath = path;
                _cachedStamp = stamp;
            }

            return _cachedSlices is not null && _cachedSlices.TryGetValue(field, out byte[]? bytes)
                ? new DataPayload(bytes, "application/json")
                : null;
        }
    }

    private static Dictionary<string, byte[]>? Build(string path, string route) {
        JsonNode? root;
        try {
            root = JsonNode.Parse(File.ReadAllText(path));
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
