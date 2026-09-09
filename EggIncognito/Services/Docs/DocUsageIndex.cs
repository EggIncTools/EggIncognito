using EggIncognito.Core.Services;
using EggIncognito.Models.Docs;

namespace EggIncognito.Services.Docs;

public interface IDocUsageIndex {
    bool IsKnown(string? message);
    IReadOnlyList<MessageEndpointUse> EndpointsUsing(string message);
    IReadOnlyList<MessageFieldRef> ReferencedBy(string message);
    IReadOnlyList<SchemaField> FieldsOf(string message);
    IReadOnlyList<string> NestedOf(string message);
    string? ParentOf(string message);
}

public sealed class DocUsageIndex(IRouteCatalog routes, IProtoReflection proto, IBinaryRouteProvider? binary = null)
    : IDocUsageIndex {
    private static readonly TimeSpan UsesTtl = TimeSpan.FromSeconds(15);

    private readonly Lazy<HashSet<string>> _known = new(() =>
        [with(StringComparer.Ordinal), .. proto.AllMessageTypes().Select(t => t.Name)]);

    private readonly Lazy<Dictionary<string, List<string>>> _nested = new(() => BuildNested(proto));
    private readonly Lazy<Dictionary<string, string>> _parents = new(() => BuildParents(proto));

    private readonly Lazy<Dictionary<string, List<MessageFieldRef>>> _refs = new(() => BuildRefs(proto));

    private readonly TtlSnapshot<Dictionary<string, List<MessageEndpointUse>>> _uses =
        new(UsesTtl, () => BuildUses(routes, binary));

    public bool IsKnown(string? message) => message is not null && _known.Value.Contains(message);

    public IReadOnlyList<MessageEndpointUse> EndpointsUsing(string message) =>
        _uses.Get().GetValueOrDefault(message) ?? [];

    public IReadOnlyList<MessageFieldRef> ReferencedBy(string message) =>
        _refs.Value.GetValueOrDefault(message) ?? [];

    public IReadOnlyList<SchemaField> FieldsOf(string message) => proto.Schema(message)?.Fields ?? [];

    public IReadOnlyList<string> NestedOf(string message) => _nested.Value.GetValueOrDefault(message) ?? [];

    public string? ParentOf(string message) => _parents.Value.GetValueOrDefault(message);

    private static Dictionary<string, List<MessageEndpointUse>> BuildUses(IRouteCatalog routes,
        IBinaryRouteProvider? binary) {
        var map = new Dictionary<string, List<MessageEndpointUse>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in routes.All()) {
            seen.Add(r.Path);
            AddRoute(map, r.Path, r.Request, r.Response, r.RequestWrapped, r.ResponseWrapped, r);
        }

        foreach (var b in binary?.AllBinaryRoutes() ?? []) {
            if (!seen.Add(b.Path)) continue;
            var (request, requestWrapped) = Normalize(b.Request, b.RequestWrapped);
            var (response, responseWrapped) = Normalize(b.Response, b.ResponseWrapped);
            AddRoute(map, b.Path, request, response, requestWrapped, responseWrapped, null);
        }

        return map;
    }

    private static (string? Type, bool Wrapped) Normalize(string? type, bool wrapped) =>
        type == RouteCatalog.WrapperMessage ? (null, true) : (type, wrapped);

    private static void AddRoute(Dictionary<string, List<MessageEndpointUse>> map, string path, string? request,
        string? response, bool requestWrapped, bool responseWrapped, RouteInfo? route) {
        bool locked = requestWrapped || responseWrapped;
        if (request is not null && request == response) {
            Add(map, request, new MessageEndpointUse(path, MessageUseRole.Both, route, locked));
        } else {
            if (request is not null) Add(map, request, new MessageEndpointUse(path, MessageUseRole.Request, route, locked));
            if (response is not null) Add(map, response, new MessageEndpointUse(path, MessageUseRole.Response, route, locked));
        }

        if (WrapRole(requestWrapped, responseWrapped) is { } wrap) {
            Add(map, RouteCatalog.WrapperMessage, new MessageEndpointUse(path, wrap, route, locked));
        }
    }

    private static MessageUseRole? WrapRole(bool requestWrapped, bool responseWrapped) =>
        (requestWrapped, responseWrapped) switch {
            (true, true) => MessageUseRole.WrapsBoth,
            (true, false) => MessageUseRole.WrapsRequest,
            (false, true) => MessageUseRole.WrapsResponse,
            _ => null
        };

    private static Dictionary<string, List<MessageFieldRef>> BuildRefs(IProtoReflection proto) {
        var map = new Dictionary<string, List<MessageFieldRef>>(StringComparer.Ordinal);
        foreach (var info in proto.AllMessageTypes()) {
            var schema = proto.Schema(info.Name);
            if (schema is null) continue;
            foreach (var f in schema.Fields) {
                if (f.Type != "message" || f.MessageType is null) continue;
                Add(map, f.MessageType, new MessageFieldRef(info.Name, f.Name, f.Repeated));
            }
        }

        return map;
    }

    private static Dictionary<string, List<string>> BuildNested(IProtoReflection proto) {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var info in proto.AllMessageTypes()) {
            if (info.Parent is null) continue;
            Add(map, info.Parent, info.Name);
        }

        return map;
    }

    private static Dictionary<string, string> BuildParents(IProtoReflection proto) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var info in proto.AllMessageTypes()) {
            if (info.Parent is not null) map[info.Name] = info.Parent;
        }

        return map;
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T item) {
        if (!map.TryGetValue(key, out var list)) {
            list = [];
            map[key] = list;
        }

        list.Add(item);
    }
}
