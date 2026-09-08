using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Core.Services;

public sealed record RouteInfo(
    string Path,
    string? Request,
    string? Response,
    bool RequestWrapped,
    bool ResponseWrapped,
    string? RawResponse,
    bool PathParam,
    bool PathParamOnly) {
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public interface IRouteCatalog {
    IReadOnlyList<RouteInfo> All();
    RouteInfo? Resolve(string path);
}

public sealed partial class RouteCatalog : IRouteCatalog {
    public const string WrapperMessage = "AuthenticatedMessage";
    private readonly Dictionary<string, RouteInfo> _byPath;
    private readonly IReadOnlyList<RouteInfo> _routes;

    public RouteCatalog(IConfiguration config)
        : this(ResolveYamlPath(config)) {
    }

    internal RouteCatalog(string yamlPath) {
        string text = File.Exists(yamlPath) ? File.ReadAllText(yamlPath) : "";
        var list = text.Length == 0 ? [] : Parse(text);
        _routes = list;
        _byPath = list.ToDictionary(e => e.Path, StringComparer.Ordinal);
        ExcludedPaths = text.Length == 0 ? [] : ParseExcluded(text);
    }

    public IReadOnlyList<RouteInfo> All() => _routes;

    public IReadOnlyList<string> ExcludedPaths { get; }

    public RouteInfo? Resolve(string path) =>
        _byPath.GetValueOrDefault(path);

    public static RouteCatalog ForRepo(string contentRoot) =>
        new(ContentRoot.RoutesYamlPath(contentRoot));

    private static string ResolveYamlPath(IConfiguration config) =>
        ContentRoot.ResolveRouteMapFile(config["RoutesYamlPath"], "routes.yaml");

    internal static List<RouteInfo> Parse(string yaml) {
        var result = new List<RouteInfo>();
        Block? b = null;
        bool inRoutes = false;

        void Flush() {
            if (b?.Path is not null) result.Add(Emit(b));
            b = null;
        }

        foreach (string rawLine in yaml.Split('\n')) {
            string line = rawLine.TrimEnd('\r');

            var topKey = TopKeyRegex().Match(line);
            if (topKey.Success) {
                Flush();
                inRoutes = topKey.Groups[1].Value == "routes";
                continue;
            }

            if (!inRoutes) continue;

            var pathMatch = MyRegex().Match(line);
            if (pathMatch.Success) {
                Flush();
                string path = pathMatch.Groups[1].Value.Trim().TrimEnd('/');
                b = new Block { Path = path.Length == 0 ? null : path };
                continue;
            }

            if (b is null) continue;

            ApplyLine(b, line);
        }

        Flush();
        return result;
    }

    private static void ApplyLine(Block b, string line) {
        string? V(string key) {
            var m = Regex.Match(line, @"^\s+" + Regex.Escape(key) + @":\s*([^#]*?)\s*(?:#.*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(2));
            return m.Success ? m.Groups[1].Value : null;
        }

        if (b.InAliases) {
            var item = AliasItemRegex().Match(line);
            if (item.Success) {
                if (item.Groups[1].Value.Length > 0) b.Aliases.Add(item.Groups[1].Value);
                return;
            }

            b.InAliases = false;
        }

        string? v;
        if ((v = V("requestType")) is not null) {
            b.LegacyReq = v;
        } else if ((v = V("responseType")) is not null) {
            b.LegacyRes = v;
        } else if ((v = V("request")) is not null) {
            b.Request = NullIfEmpty(v);
            b.HasRequest = true;
        } else if ((v = V("response")) is not null) {
            b.Response = NullIfEmpty(v);
            b.HasResponse = true;
        } else if ((v = V("requestWrapped")) is not null) {
            b.RequestWrapped = v == "true";
        } else if ((v = V("responseWrapped")) is not null) {
            b.ResponseWrapped = v == "true";
        } else if ((v = V("rawResponse")) is not null) {
            b.RawResponse = v.Trim('"');
        } else if ((v = V("pathParamOnly")) is not null) {
            b.PathParamOnly = v == "true";
        } else if ((v = V("pathParam")) is not null) {
            b.PathParam = v == "true";
        } else if (V("aliases") is not null) {
            b.InAliases = true;
        }
    }

    internal static List<string> ParseExcluded(string yaml) {
        var result = new List<string>();
        bool inExcluded = false;

        foreach (string rawLine in yaml.Split('\n')) {
            string line = rawLine.TrimEnd('\r');

            var topKey = TopKeyRegex().Match(line);
            if (topKey.Success) {
                inExcluded = topKey.Groups[1].Value == "excluded";
                continue;
            }

            if (!inExcluded) continue;

            var item = AliasItemRegex().Match(line);
            if (item.Success && item.Groups[1].Value.Length > 0) result.Add(item.Groups[1].Value);
        }

        return result;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    private static RouteInfo Emit(Block b) {
        (string? reqType, bool reqWrapDefault) = Normalize(b.HasRequest ? b.Request : b.LegacyReq);
        (string? resType, bool resWrapDefault) = Normalize(b.HasResponse ? b.Response : b.LegacyRes);
        return new RouteInfo(
            b.Path!,
            b.PathParamOnly ? null : reqType,
            resType,
            b.RequestWrapped ?? reqWrapDefault,
            b.ResponseWrapped ?? resWrapDefault,
            b.RawResponse,
            b.PathParam,
            b.PathParamOnly) { Aliases = b.Aliases };
    }

    private static (string? type, bool wrapped) Normalize(string? v) {
        if (v is null) return (null, false);
        if (v == WrapperMessage) return (null, true);
        return (v, false);
    }

    [GeneratedRegex(@"^(\w[\w_]*):\s*$")]
    private static partial Regex TopKeyRegex();

    [GeneratedRegex(@"^\s+-\s+path:\s*(.*?)\s*$")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"^\s+-\s*([^#]*?)\s*(?:#.*)?$")]
    private static partial Regex AliasItemRegex();

    private sealed class Block {
        public readonly List<string> Aliases = [];
        public string? Path, Request, Response, RawResponse, LegacyReq, LegacyRes;
        public bool PathParam, PathParamOnly, HasRequest, HasResponse, InAliases;
        public bool? RequestWrapped, ResponseWrapped;
    }
}
