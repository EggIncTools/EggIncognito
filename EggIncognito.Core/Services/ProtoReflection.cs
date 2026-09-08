using System.Collections.Concurrent;
using System.Reflection;
using Ei;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core.Services;

public sealed record SchemaEnumValue(string Name, int Number);

public sealed record SchemaField(
    string Name,
    string JsonName,
    int Number,
    string Type,
    bool Repeated,
    bool Required,
    string? MessageType,
    IReadOnlyList<SchemaEnumValue>? EnumValues);

public sealed record SchemaMessage(string Name, IReadOnlyList<SchemaField> Fields);

public interface IProtoReflection {
    MessageDescriptor? FindMessage(string typeName);
    MessageParser? FindParser(string typeName);
    SchemaMessage? Schema(string typeName);

    IReadOnlyList<string> AllMessageTypeNames();
    IReadOnlyList<MessageTypeInfo> AllMessageTypes();
}

public sealed class ProtoReflection : IProtoReflection {
    private static readonly Assembly EiAssembly = typeof(AuthenticatedMessage).Assembly;

    private static readonly ConcurrentDictionary<string, (MessageDescriptor Descriptor, MessageParser Parser)> Cache =
        new(StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyList<string>> AllNames = new(() =>
        EiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, DeclaringType: null }
                        && t.Namespace == "Ei"
                        && typeof(IMessage).IsAssignableFrom(t)
                        && t.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static) is not null)
            .Select(t => t.Name)
            .Distinct()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList());

    private static readonly Lazy<IReadOnlyList<MessageTypeInfo>> AllTypes = new(() => {
        var list = new List<MessageTypeInfo>();
        foreach (var t in EiAssembly.GetTypes().Where(t => t.DeclaringType is null && t.Namespace == "Ei"))
            Collect(t, list);

        return list
            .DistinctBy(i => i.Name, StringComparer.Ordinal)
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    public IReadOnlyList<string> AllMessageTypeNames() => AllNames.Value;

    public IReadOnlyList<MessageTypeInfo> AllMessageTypes() => AllTypes.Value;

    public MessageDescriptor? FindMessage(string typeName) => Resolve(typeName)?.Descriptor;

    public MessageParser? FindParser(string typeName) => Resolve(typeName)?.Parser;

    public SchemaMessage? Schema(string typeName) {
        var desc = FindMessage(typeName);
        if (desc is null) return null;

        var fields = desc.Fields.InFieldNumberOrder().Select(ToSchemaField).ToList();
        return new SchemaMessage(desc.Name, fields);
    }

    private static void Collect(Type clr, List<MessageTypeInfo> into) {
        if (DescriptorOf(clr) is not { } desc) return;
        into.Add(new MessageTypeInfo(Relative(desc), desc.ContainingType is { } parent ? Relative(parent) : null));

        var nested = clr.GetNestedType("Types", BindingFlags.Public);
        if (nested is null) return;
        foreach (var t in nested.GetNestedTypes(BindingFlags.Public)) Collect(t, into);
    }

    private static MessageDescriptor? DescriptorOf(Type clr) {
        if (clr is not { IsClass: true, IsAbstract: false } || !typeof(IMessage).IsAssignableFrom(clr)) return null;
        return clr.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            as MessageDescriptor;
    }

    private static string Relative(MessageDescriptor desc) {
        string package = desc.File.Package;
        string full = desc.FullName;
        if (package.Length == 0) return full;
        string prefix = package + ".";
        return full.StartsWith(prefix, StringComparison.Ordinal) ? full[prefix.Length..] : full;
    }

    private static string Short(string typeName) =>
        typeName.StartsWith("Ei.", StringComparison.Ordinal) ? typeName[3..] : typeName;

    private static (MessageDescriptor Descriptor, MessageParser Parser)? Resolve(string typeName) {
        string key = Short(typeName);
        if (Cache.TryGetValue(key, out var hit)) return hit;

        var clr = EiAssembly.GetType("Ei." + key.Replace(".", "+Types+", StringComparison.Ordinal));
        if (clr?.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) is not MessageDescriptor descriptor || clr
                ?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) is not MessageParser parser) {
            return null;
        }

        Cache.TryAdd(key, (descriptor, parser));
        return (descriptor, parser);
    }

    private static SchemaField ToSchemaField(FieldDescriptor f) {
        string type = f.FieldType.ToString().ToLowerInvariant();
        string? messageType = null;
        IReadOnlyList<SchemaEnumValue>? enumValues = null;

        if (f.FieldType is FieldType.Message or FieldType.Group) {
            type = "message";
            messageType = Relative(f.MessageType);
        } else if (f.FieldType == FieldType.Enum) {
            type = "enum";
            enumValues = f.EnumType.Values
                .Select(v => new SchemaEnumValue(v.Name, v.Number))
                .ToList();
        }

        return new SchemaField(
            f.Name,
            f.JsonName,
            f.FieldNumber,
            type,
            f.IsRepeated,
            f.IsRequired,
            messageType,
            enumValues);
    }
}
