using EggIncognito.Core.Services.ProtoExtract;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core.Services;

public static class ProtoEnumIndex {
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> Parse(string protoText) {
        var enums = new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(protoText)) return enums;

        FileDescriptorProto descriptor;
        try {
            descriptor = ProtoTextCompiler.Compile(protoText);
        } catch {
            return enums;
        }

        string? package = string.IsNullOrEmpty(descriptor.Package) ? null : descriptor.Package;
        foreach (var enumType in descriptor.EnumType) {
            AddEnum(enums, package, enumType);
        }

        foreach (var messageType in descriptor.MessageType) {
            CollectFromMessage(enums, package, messageType);
        }

        return enums;
    }

    private static void CollectFromMessage(Dictionary<string, IReadOnlyDictionary<int, string>> enums, string? package, DescriptorProto messageType) {
        string prefix = string.IsNullOrEmpty(package) ? messageType.Name : $"{package}.{messageType.Name}";
        foreach (var enumType in messageType.EnumType) {
            AddEnum(enums, prefix, enumType);
        }

        foreach (var nestedType in messageType.NestedType) {
            CollectFromMessage(enums, prefix, nestedType);
        }
    }

    private static void AddEnum(Dictionary<string, IReadOnlyDictionary<int, string>> enums, string? prefix, EnumDescriptorProto enumType) {
        string fullName = string.IsNullOrEmpty(prefix) ? enumType.Name : $"{prefix}.{enumType.Name}";
        var members = new Dictionary<int, string>();
        foreach (var value in enumType.Value) {
            members.TryAdd(value.Number, value.Name);
        }

        enums[fullName] = members;
    }
}
