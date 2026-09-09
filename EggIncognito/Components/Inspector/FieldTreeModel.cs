using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EggIncognito.Core.Services;
using EggIncognito.Services.Inspector;

namespace EggIncognito.Components.Inspector;

public sealed class FieldNode {
    public const string RequestInfoType = "BasicRequestInfo";

    public required SchemaField Field { get; init; }

    public required string PathKey { get; init; }

    public string Value { get; set; } = "";

    public List<string> Items { get; set; } = [];

    public List<FieldNode> Children { get; set; } = [];

    public bool Locked { get; set; }

    public bool IsMessage => Field.Type == "message";
    public bool IsRequestInfo => IsMessage && Field.MessageType == RequestInfoType;
    public bool IsEnum => Field.Type == "enum";
    public bool IsBool => Field.Type == "bool";
    public bool IsRepeated => Field.Repeated && !IsMessage;

    public void AddItem() => Items.Add("");

    public void RemoveItem(int i) {
        if (i >= 0 && i < Items.Count) Items.RemoveAt(i);
    }
}

public static class FieldTreeBuilder {
    private static readonly HashSet<string> Int32 =
        ["int32", "uint32", "sint32", "fixed32", "sfixed32"];

    private static readonly HashSet<string> Int64 =
        ["int64", "uint64", "sint64", "fixed64", "sfixed64"];

    private static readonly HashSet<string> Floats = ["double", "float"];

    public static List<FieldNode> Build(SchemaMessage schema, Func<string, SchemaMessage?> schemaOf) =>
        [.. schema.Fields.Select(f => BuildNode(f, [], schemaOf))];

    private static FieldNode BuildNode(SchemaField f, IReadOnlyList<string> path,
        Func<string, SchemaMessage?> schemaOf) {
        var chain = path.Append(f.JsonName).ToList();
        var node = new FieldNode { Field = f, PathKey = string.Join(".", chain) };
        if (f.Type == "message" && f.MessageType is not null) {
            var sub = schemaOf(f.MessageType);
            if (sub is not null)
                node.Children = [.. sub.Fields.Select(cf => BuildNode(cf, chain, schemaOf))];
        }

        return node;
    }

    private static JsonValue? Coerce(string raw, string ptype) {
        if (string.IsNullOrEmpty(raw)) return null;
        if (ptype == "bool") return JsonValue.Create(raw == "true");
        if (Int32.Contains(ptype))
            return int.TryParse(raw, out int i) ? JsonValue.Create(i) : JsonValue.Create(raw);
        return Int64.Contains(ptype)
            ? JsonValue.Create(raw)
            : Floats.Contains(ptype)
                ? double.TryParse(raw, out double d) ? JsonValue.Create(d) : JsonValue.Create(raw)
                : JsonValue.Create(raw);
    }

    public static JsonObject Collect(IReadOnlyList<FieldNode> nodes) {
        var obj = new JsonObject();
        foreach (var n in nodes) {
            if (n.IsMessage) {
                var child = Collect(n.Children);
                if (child.Count > 0) obj[n.Field.JsonName] = child;
            } else if (n.Field.Repeated) {
                var arr = new JsonArray();
                foreach (string item in n.Items) {
                    var v = Coerce(item, n.Field.Type);
                    if (v is not null) arr.Add(v);
                }

                if (arr.Count > 0) obj[n.Field.JsonName] = arr;
            } else {
                var v = Coerce(n.Value, n.Field.Type);
                if (v is not null) obj[n.Field.JsonName] = v;
            }
        }

        return obj;
    }

    public static void Apply(IReadOnlyList<FieldNode> nodes, JsonObject obj) {
        foreach (var n in nodes) {
            obj.TryGetPropertyValue(n.Field.JsonName, out var v);
            if (n.IsMessage)
                Apply(n.Children, v as JsonObject ?? []);
            else if (n.Field.Repeated)
                n.Items = v is JsonArray arr ? [.. arr.Select(ValueText)] : [];
            else
                n.Value = ValueText(v);
        }
    }

    private static string ValueText(JsonNode? v) => v switch {
        null => "",
        JsonValue jv when jv.TryGetValue(out string? s) => s ?? "",
        _ => v.ToJsonString()
    };

    public static void ApplyEnvLock(IReadOnlyList<FieldNode> nodes, IReadOnlyDictionary<string, string> env) {
        var rinfo = nodes.FirstOrDefault(n => n.Field.JsonName == "rinfo" && n.IsMessage);
        if (rinfo is null) return;
        foreach (var child in rinfo.Children) {
            if (env.TryGetValue(child.Field.JsonName, out string? v) && !string.IsNullOrEmpty(v)) {
                child.Value = v;
                child.Locked = true;
            } else {
                child.Locked = false;
            }
        }
    }

    public static void ApplyEnvDefaults(IReadOnlyList<FieldNode> nodes, IReadOnlyDictionary<string, string> env) {
        foreach (var n in nodes) {
            if (n.Field.JsonName == "rinfo") continue;
            if (n.IsMessage || n.Field.Repeated) continue;
            if (!string.IsNullOrEmpty(n.Value)) continue;
            if (env.TryGetValue(n.Field.JsonName, out string? v) && !string.IsNullOrEmpty(v))
                n.Value = v;
        }
    }
}

public sealed partial class EnvRow {
    public required string Key { get; init; }
    public required EnvValueType ValueType { get; init; }
    public string Value { get; set; } = "";

    public EnvEditor Editor { get; init; } = EnvEditor.Text;

    public IReadOnlyList<string>? Options { get; init; }

    public string? Hint { get; init; }

    public bool IsInvalid() {
        return !string.IsNullOrEmpty(Value) && Editor switch {
            EnvEditor.Int => !int.TryParse(Value, out _),
            EnvEditor.Version => !VersionRegex().IsMatch(Value),
            EnvEditor.Build => !BuildRegex().IsMatch(Value),
            EnvEditor.Code => !MyRegex().IsMatch(Value),
            EnvEditor.Select => Options is not null && !Options.Contains(Value),
            EnvEditor.Eid => !EidPattern.Exact.IsMatch(Value),
            _ => false
        };
    }

    [GeneratedRegex(@"^\d+(\.\d+){1,3}$")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"^\d+(\.\d+){0,3}$")]
    private static partial Regex BuildRegex();

    [GeneratedRegex("^[A-Za-z]{2,3}$")]
    private static partial Regex MyRegex();
}

public static class EnvCollector {
    public static Dictionary<string, object?> Collect(IEnumerable<EnvRow> rows) {
        var env = new Dictionary<string, object?>();
        foreach (var r in rows) {
            object? v = r.ValueType switch {
                EnvValueType.Number => int.TryParse(r.Value, out int i) ? i : r.Value,
                EnvValueType.Boolean => r.Value == "true",
                _ => r.Value
            };
            env[r.Key] = v;
        }

        return env;
    }

    public static Dictionary<string, string> AsStrings(IEnumerable<EnvRow> rows) =>
        rows.ToDictionary(r => r.Key, r => r.Value);
}
