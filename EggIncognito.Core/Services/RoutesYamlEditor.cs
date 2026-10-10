using System.Text;
using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services;

public sealed partial class RoutesYamlEditor {
    private const string NoneMarker = "# none - empty body";
    private readonly List<string> _lines;
    private readonly string _path;
    private DateTime _loadedStampUtc;
    private Dictionary<string, RouteInfo>? _parsed;

    public RoutesYamlEditor(string contentRoot) {
        _path = ContentRoot.RoutesYamlPath(contentRoot);
        _lines = [.. File.ReadAllText(_path).Replace("\r\n", "\n").Split('\n')];
        _loadedStampUtc = File.GetLastWriteTimeUtc(_path);
    }

    public bool Dirty { get; private set; }

    public void Save() {
        if (!Dirty) return;

        if (File.GetLastWriteTimeUtc(_path) != _loadedStampUtc) {
            throw new IOException(
                $"routes.yaml changed on disk since load; aborting save to avoid clobbering concurrent edits: {_path}");
        }

        File.WriteAllText(_path, string.Join('\n', _lines), new UTF8Encoding(false));
        Dirty = false;
        _loadedStampUtc = File.GetLastWriteTimeUtc(_path);
    }

    public bool HasPath(string path) => FindRouteStart(path) >= 0;

    public bool SetFieldIfEmpty(string path, string key, string value) {
        (int start, int end) = RouteBlock(path);
        if (start < 0) return false;

        string legacy = LegacyAlias(key);
        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k],
                @"^(\s*)(" + Regex.Escape(key) + "|" + Regex.Escape(legacy) + @"):\s*([^#]*?)\s*(?:#.*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(2));
            if (!m.Success) continue;

            string existing = m.Groups[3].Value.Trim();
            if (existing.Length > 0 && existing != "AuthenticatedMessage")
                return false;
            _lines[k] = $"{m.Groups[1].Value}{key}: {value}";
            Mutated();
            return true;
        }

        _lines.Insert(start + 1, $"{FieldIndent(start, end)}{key}: {value}");
        Mutated();
        return true;
    }

    public bool SetWrappedFlag(string path, string key) {
        (int start, int end) = RouteBlock(path);
        if (start < 0) return false;

        for (int k = start + 1; k < end; k++) {
            var m = Regex.Match(_lines[k], @"^\s*" + Regex.Escape(key) + @":\s*([^#]*?)\s*(?:#.*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(2));
            if (m.Success) return false;
        }

        _lines.Insert(start + 1, $"{FieldIndent(start, end)}{key}: true");
        Mutated();
        return true;
    }

    public string CanonicalPath(string path) {
        if (HasPath(path)) return path;
        int slash = path.LastIndexOf('/');
        if (slash <= 0) return path;
        string parent = path[..slash];
        return HasPathParam(parent) ? parent : path;
    }

    private bool HasPathParam(string path) => Info(path)?.PathParam == true;

    public bool AddRoute(string path, string? request, bool requestWrapped, string response, bool responseWrapped) {
        if (HasPath(path)) return false;
        if (CanonicalPath(path) != path) return false;

        string ns = path.Split('/')[0];
        int insertAt = SectionInsertPoint(ns);
        if (insertAt < 0) return false;

        var block = new List<string> {
            $"  - path: {path}",
            string.IsNullOrEmpty(request)
                ? "    request:  # TODO review - request type not detected"
                : $"    request: {request}"
        };
        if (requestWrapped) block.Add("    requestWrapped: true");
        block.Add($"    response: {response}");
        if (responseWrapped) block.Add("    responseWrapped: true");

        _lines.InsertRange(insertAt, block);
        Mutated();
        return true;
    }

    public bool RemoveFromNeedsCapture(string path) {
        int nc = _lines.FindIndex(l => l.StartsWith("needs_capture:", StringComparison.Ordinal));
        if (nc < 0) return false;
        int end = NextTopLevelKey(nc);

        for (int k = nc + 1; k < end; k++) {
            var m = MyRegex().Match(_lines[k]);
            if (m.Success && m.Groups[1].Value == path) {
                _lines.RemoveAt(k);
                Mutated();
                return true;
            }
        }

        return false;
    }

    public bool MarkRequestNone(string path) {
        (int start, int end) = RouteBlock(path);
        if (start < 0) return false;
        for (int k = start + 1; k < end; k++) {
            var m = RequestFieldRegex().Match(_lines[k]);
            if (!m.Success) continue;
            if (m.Groups[3].Value.Trim().Length > 0) return false;
            _lines[k] = $"{m.Groups[1].Value}request:  {NoneMarker}";
            Mutated();
            return true;
        }

        _lines.Insert(start + 1, $"{FieldIndent(start, end)}request:  {NoneMarker}");
        Mutated();
        return true;
    }

    public bool RequestUnresolved(string path) {
        (int start, int end) = RouteBlock(path);
        if (start >= 0) {
            for (int k = start + 1; k < end; k++) {
                if (Regex.IsMatch(_lines[k], @"^\s*request:\s*" + Regex.Escape(NoneMarker) + @"\s*$",
                        RegexOptions.None, TimeSpan.FromSeconds(2))) {
                    return false;
                }
            }
        }

        return Info(path)?.Request is null;
    }

    public bool ResponseUnresolved(string path) => Info(path)?.Response is null;

    private RouteInfo? Info(string path) {
        _parsed ??= RouteCatalog.Parse(string.Join('\n', _lines))
            .ToDictionary(r => r.Path, StringComparer.Ordinal);
        return _parsed.GetValueOrDefault(path);
    }

    private void Mutated() {
        Dirty = true;
        _parsed = null;
    }

    private string FieldIndent(int start, int end) {
        for (int k = start + 1; k < end; k++) {
            var m = IndentedLineRegex().Match(_lines[k]);
            if (m.Success) return m.Groups[1].Value;
        }

        return LeadingWhitespaceRegex().Match(_lines[start]).Value + "  ";
    }

    private static string LegacyAlias(string key) => key switch {
        "request" => "requestType",
        "response" => "responseType",
        _ => key
    };

    private int FindRouteStart(string path) =>
        _lines.FindIndex(l => Regex.IsMatch(l, @"^\s*-\s+path:\s+" + Regex.Escape(path) + @"\s*$",
            RegexOptions.None, TimeSpan.FromSeconds(2)));

    private (int start, int end) RouteBlock(string path) {
        int start = FindRouteStart(path);
        if (start < 0) return (-1, -1);
        int end = _lines.Count;
        for (int k = start + 1; k < _lines.Count; k++) {
            if (PathItemRegex().IsMatch(_lines[k]) || TopLevelLineRegex().IsMatch(_lines[k])) {
                end = k;
                break;
            }
        }

        return (start, end);
    }

    private int NextTopLevelKey(int from) {
        for (int k = from + 1; k < _lines.Count; k++) {
            if (TopLevelKeyRegex().IsMatch(_lines[k]))
                return k;
        }

        return _lines.Count;
    }

    private int SectionInsertPoint(string ns) {
        int routes = _lines.FindIndex(l => l.StartsWith("routes:", StringComparison.Ordinal));
        if (routes < 0) return -1;
        int routesEnd = NextTopLevelKey(routes);

        int comment = _lines.FindIndex(routes, l => l.Trim() == $"# {ns}/");
        if (comment < 0 || comment >= routesEnd)
            return routesEnd;

        int at = routesEnd;
        for (int k = comment + 1; k < routesEnd; k++) {
            if (_lines[k].TrimStart().StartsWith("# ", StringComparison.Ordinal) && _lines[k].Trim().EndsWith('/')) {
                at = k;
                break;
            }
        }

        while (at > comment && _lines[at - 1].Trim().Length == 0) at--;
        return at;
    }

    [GeneratedRegex(@"^\s*-\s+(\S+)\s*(?:#.*)?$")]
    private static partial Regex MyRegex();

    [GeneratedRegex(@"^(\s*)(request|requestType):\s*([^#]*?)\s*(?:#.*)?$")]
    private static partial Regex RequestFieldRegex();

    [GeneratedRegex(@"^(\s+)\S")]
    private static partial Regex IndentedLineRegex();

    [GeneratedRegex(@"^\s*")]
    private static partial Regex LeadingWhitespaceRegex();

    [GeneratedRegex(@"^\s*-\s+path:")]
    private static partial Regex PathItemRegex();

    [GeneratedRegex(@"^\w")]
    private static partial Regex TopLevelLineRegex();

    [GeneratedRegex(@"^\w[\w_]*:")]
    private static partial Regex TopLevelKeyRegex();
}
