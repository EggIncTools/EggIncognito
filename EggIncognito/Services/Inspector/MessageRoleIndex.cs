using EggIncognito.Core.Services;
using EggIncognito.Models.Inspector;

namespace EggIncognito.Services.Inspector;

public interface IMessageRoleIndex {
    MessageRoles Snapshot();
}

public sealed class MessageRoleIndex(IProtoReflection proto, IRouteCatalog routes, IBinaryRouteProvider? binary = null) : IMessageRoleIndex {
    private const string RequestSuffix = "Request";
    private const string ResponseSuffix = "Response";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);

    private readonly TtlSnapshot<MessageRoles> _snapshot = new(Ttl, () => Compute(proto, routes, binary));

    public MessageRoles Snapshot() => _snapshot.Get();

    private static MessageRoles Compute(IProtoReflection proto, IRouteCatalog routes, IBinaryRouteProvider? binary) {
        var request = new HashSet<string>(StringComparer.Ordinal);
        var response = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in routes.All()) {
            Note(request, r.Request);
            Note(response, r.Response);
        }

        foreach (var b in binary?.AllBinaryRoutes() ?? []) {
            Note(request, b.Request);
            Note(response, b.Response);
        }

        var all = proto.AllMessageTypeNames().Where(n => n != RouteCatalog.WrapperMessage).ToList();
        foreach (string n in all) {
            if (n.EndsWith(RequestSuffix, StringComparison.Ordinal)) request.Add(n);
            if (n.EndsWith(ResponseSuffix, StringComparison.Ordinal)) response.Add(n);
        }

        var other = all.Where(n => !request.Contains(n) && !response.Contains(n)).ToList();
        var requestList = Sorted(request);
        var responseList = Sorted(response);
        return new MessageRoles(requestList, responseList, other,
            Others(request, responseList, other),
            Others(response, requestList, other));
    }

    private static List<string> Others(HashSet<string> taken, IReadOnlyList<string> secondary,
        IReadOnlyList<string> other) {
        var list = secondary.Concat(other).Where(n => !taken.Contains(n)).Distinct(StringComparer.Ordinal).ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static void Note(HashSet<string> set, string? name) {
        if (!string.IsNullOrEmpty(name) && name != RouteCatalog.WrapperMessage) set.Add(name);
    }

    private static List<string> Sorted(HashSet<string> set) {
        var list = set.ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }
}
