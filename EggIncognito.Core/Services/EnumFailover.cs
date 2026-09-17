using System.Collections;
using System.Globalization;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Microsoft.Extensions.Logging;

namespace EggIncognito.Core.Services;

public sealed record LatestProtoText(string Platform, string Build, string ProtoText);

public interface ILastKnownProtoSource {
    Task<IReadOnlyList<LatestProtoText>> GetLatestProtosAsync(CancellationToken ct = default);
}

public interface IEnumFailover {
    string Apply(IMessage message, string formattedJson);
}

public sealed class EnumFailover(ILastKnownProtoSource source, ILogger<EnumFailover>? logger = null) : IEnumFailover {
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private volatile IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? _map;
    private string? _staleKey;
    private long _nextRefreshTicks;

    public string Apply(IMessage message, string formattedJson) {
        if (message is null || string.IsNullOrEmpty(formattedJson)) return formattedJson;
        try {
            var map = EnsureMap();
            if (map is null || map.Count == 0) return formattedJson;
            if (JsonNode.Parse(formattedJson) is not JsonObject obj) return formattedJson;
            AnnotateMessage(message, obj, map);
            return obj.ToJsonString();
        } catch {
            return formattedJson;
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? EnsureMap() {
        var current = _map;
        if (current is null) {
            Rebuild();
            return _map;
        }

        long now = DateTimeOffset.UtcNow.Ticks;
        if (now >= Interlocked.Read(ref _nextRefreshTicks)) {
            Interlocked.Exchange(ref _nextRefreshTicks, now + RefreshInterval.Ticks);
            _ = Task.Run(() => {
                try {
                    Rebuild();
                } catch (Exception ex) {
                    logger?.LogEnumMapRefreshFailed(ex);
                }
            });
        }

        return current;
    }

    private void Rebuild() {
        var protos = source.GetLatestProtosAsync().GetAwaiter().GetResult();
        string key = string.Join("|", protos
            .OrderBy(p => p.Platform, StringComparer.Ordinal)
            .Select(p => p.Platform + ":" + p.Build));

        lock (_gate) {
            if (_map is not null && key == _staleKey) return;

            var merged = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
            foreach (var proto in protos) {
                foreach (var (enumName, members) in ProtoEnumIndex.Parse(proto.ProtoText)) {
                    if (!merged.TryGetValue(enumName, out var target)) {
                        target = [];
                        merged[enumName] = target;
                    }

                    foreach (var (num, name) in members) target.TryAdd(num, name);
                }
            }

            _map = merged.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyDictionary<int, string>)kv.Value,
                StringComparer.Ordinal);
            _staleKey = key;
        }
    }

    private static void AnnotateMessage(
        IMessage message, JsonObject obj, IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> map) {
        foreach (var field in message.Descriptor.Fields.InFieldNumberOrder()) {
            if (field.IsMap) continue;
            if (!obj.TryGetPropertyValue(field.JsonName, out var valNode) || valNode is null) continue;

            if (field.FieldType == FieldType.Enum) {
                if (field.IsRepeated) {
                    if (valNode is JsonArray arr && field.Accessor.GetValue(message) is IList list) {
                        for (int i = 0; i < arr.Count && i < list.Count; i++) {
                            string? repl = Resolve(field.EnumType, Convert.ToInt32(list[i], CultureInfo.InvariantCulture), map);
                            if (repl is not null) arr[i] = JsonValue.Create(repl);
                        }
                    }
                } else {
                    string? repl = Resolve(field.EnumType, Convert.ToInt32(field.Accessor.GetValue(message), CultureInfo.InvariantCulture), map);
                    if (repl is not null) obj[field.JsonName] = JsonValue.Create(repl);
                }
            } else if (field.FieldType is FieldType.Message or FieldType.Group) {
                if (field.IsRepeated) {
                    if (valNode is JsonArray arr && field.Accessor.GetValue(message) is IList list) {
                        for (int i = 0; i < arr.Count && i < list.Count; i++) {
                            if (arr[i] is JsonObject childObj && list[i] is IMessage childMsg)
                                AnnotateMessage(childMsg, childObj, map);
                        }
                    }
                } else if (valNode is JsonObject childObj && field.Accessor.GetValue(message) is IMessage childMsg) {
                    AnnotateMessage(childMsg, childObj, map);
                }
            }
        }
    }

    private static string? Resolve(
        EnumDescriptor enumType, int number, IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> map) {
        if (enumType.FindValueByNumber(number) is not null) return null;
        return map.TryGetValue(enumType.FullName, out var members) && members.TryGetValue(number, out string? name)
            ? name
            : null;
    }
}

internal static partial class EnumFailoverLog {
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Background enum failover map refresh failed; keeping the previous map")]
    internal static partial void LogEnumMapRefreshFailed(this ILogger logger, Exception ex);
}
