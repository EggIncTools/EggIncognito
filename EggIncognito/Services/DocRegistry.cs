using EggIncognito.Core.Services;
namespace EggIncognito.Services;

public sealed record DocSubject(
    string Kind,
    string Key,
    string Title,
    string? Summary,
    IReadOnlyList<DocSubject> Children) {
    public string? Parent { get; init; }
}

public interface IDocRegistry {
    IReadOnlyList<DocSubject> Roots();
    DocSubject? Find(string kind, string key);
}

public sealed class DocRegistry : IDocRegistry {
    private readonly Dictionary<string, DocSubject> _byKey;
    private readonly IReadOnlyList<DocSubject> _roots;

    public DocRegistry(IProtoReflection proto, IRouteCatalog routes) {
        var messages = BuildMessages(proto);
        var endpoints = BuildEndpoints(routes, proto);

        _roots = [
            new DocSubject("group", "messages", "Messages", "Egg, Inc. proto message types", messages),
            new DocSubject("group", "endpoints", "Endpoints", "Mock API routes", endpoints)
        ];

        _byKey = [with(StringComparer.Ordinal)];
        foreach (var root in _roots) {
            Index(root);
            foreach (var child in root.Children) Index(child);
        }
    }

    public IReadOnlyList<DocSubject> Roots() => _roots;

    public DocSubject? Find(string kind, string key) =>
        _byKey.GetValueOrDefault($"{kind}:{key}");

    private void Index(DocSubject s) => _byKey[$"{s.Kind}:{s.Key}"] = s;

    private static List<DocSubject> BuildMessages(IProtoReflection proto) {
        var list = new List<DocSubject>();
        foreach (var info in proto.AllMessageTypes()) {
            var schema = proto.Schema(info.Name);
            var fields = schema is null
                ? (IReadOnlyList<DocSubject>)[]
                : schema.Fields.Select(FieldSubject).ToList();
            list.Add(new DocSubject("message", info.Name, info.Name, null, fields) { Parent = info.Parent });
        }

        return list;
    }

    private static DocSubject FieldSubject(SchemaField f) {
        string? typeText = f.Type == "message" && f.MessageType is not null ? f.MessageType : f.Type;
        string? summary = f.Repeated ? $"repeated {typeText}" : typeText;
        return new DocSubject("field", f.Name, f.Name, summary, []);
    }

    private static List<DocSubject> BuildEndpoints(IRouteCatalog routes, IProtoReflection proto) {
        var list = new List<DocSubject>();
        foreach (var r in routes.All()) {
            string req = r.Request ?? (r.RequestWrapped ? "AuthenticatedMessage" : "(none)");
            string res = r.Response ?? r.RawResponse ?? (r.ResponseWrapped ? "AuthenticatedMessage" : "(none)");
            string summary = $"request {req} -> response {res}";

            var children = new List<DocSubject>();
            LinkMessage(children, "request", r.Request, proto);
            LinkMessage(children, "response", r.Response, proto);

            list.Add(new DocSubject("endpoint", r.Path, r.Path, summary, children));
        }

        return list;
    }

    private static void LinkMessage(List<DocSubject> into, string role, string? typeName, IProtoReflection proto) {
        if (string.IsNullOrEmpty(typeName)) return;
        if (proto.Schema(typeName) is null) return;
        into.Add(new DocSubject("message", typeName, $"{role}: {typeName}", null, []));
    }
}
