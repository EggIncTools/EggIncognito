using EggIncognito.Core.Services;

namespace EggIncognito.Services;

public sealed class AuxbrainSurface {
    private readonly Lazy<IReadOnlyDictionary<string, RouteInfo>> _aliases;
    private readonly Lazy<IReadOnlyList<AuxbrainEntry>> _entries;
    private readonly Lazy<IReadOnlySet<string>> _namespaces;
    private readonly Lazy<string> _openApiJson;

    public AuxbrainSurface(RouteCatalog routes, IProtoReflection reflection, IConfiguration config) {
        _entries = new Lazy<IReadOnlyList<AuxbrainEntry>>(() => {
            string root = ContentRoot.Resolve(config["ContentRoot"]);
            var status = EndpointStatus.Classify(
                Path.Combine(root, "RouteMap", "routes.yaml"),
                Path.Combine(root, "Endpoints", "default"));
            return AuxbrainCatalog.Build(routes.All(), status);
        });
        _namespaces = new Lazy<IReadOnlySet<string>>(() =>
            _entries.Value.Select(e => e.Namespace).ToHashSet(StringComparer.Ordinal));
        _openApiJson = new Lazy<string>(() => OpenApiBuilder.BuildJson(_entries.Value, reflection));

        _aliases = new Lazy<IReadOnlyDictionary<string, RouteInfo>>(() => routes.All()
            .SelectMany(r => r.Aliases, (r, a) => (Alias: a, Route: r))
            .GroupBy(x => x.Alias, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Route, StringComparer.Ordinal));
    }

    public IReadOnlyList<AuxbrainEntry> Entries => _entries.Value;
    public IReadOnlySet<string> Namespaces => _namespaces.Value;
    public string OpenApiJson => _openApiJson.Value;

    public RouteInfo? ResolveAlias(string path) =>
        _aliases.Value.GetValueOrDefault(path);

    public bool IsKnownNamespace(string path) {
        int i = path.IndexOf('/');
        string ns = i < 0 ? path : path[..i];
        return Namespaces.Contains(ns);
    }
}
