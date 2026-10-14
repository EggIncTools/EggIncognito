using System.Text.RegularExpressions;
using EggIncognito.Core.Services.ProtoExtract;
using Google.Protobuf.Reflection;

namespace EggIncognito.Core.Services;

public static partial class ProtoTextIndex {
    [GeneratedRegex(@"^\s*(?:message|enum)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex DeclRegex();

    public static IReadOnlyList<string> Names(string protoText) {
        try {
            var descriptor = ProtoTextCompiler.Compile(protoText ?? "");
            List<string> names = [];
            CollectMessageNames(descriptor.MessageType, "", names);
            foreach (var e in descriptor.EnumType) names.Add(e.Name);
            return names;
        } catch {
            return DeclRegex().Matches(protoText ?? "").Select(m => m.Groups[1].Value).Distinct().ToList();
        }
    }

    private static void CollectMessageNames(IEnumerable<DescriptorProto> messages, string prefix, List<string> names) {
        foreach (var message in messages) {
            string qualifiedName = prefix.Length == 0 ? message.Name : $"{prefix}.{message.Name}";
            names.Add(qualifiedName);
            foreach (var e in message.EnumType) names.Add($"{qualifiedName}.{e.Name}");
            CollectMessageNames(message.NestedType, qualifiedName, names);
        }
    }
}
