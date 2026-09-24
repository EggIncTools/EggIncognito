using System.Text.RegularExpressions;

namespace EggIncognito.Core.Services.ProtoExtract;

public sealed partial class EndpointCatalogExtractor {
    private const string MethodPrefix = "_ZN10HttpHelper";

    [GeneratedRegex("^ei(?:_[a-z0-9]+)?(?:/[a-z0-9_]+)+$")]
    private static partial Regex PathPattern();

    public static Result Extract(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? []);
    }

    public static Result ExtractAuto(byte[] bin) {
        var img = BinaryImage.Load(bin);
        return ExtractWith(bin, img?.Symbols ?? []);
    }

    public static Result ExtractWith(byte[] bin, IReadOnlyList<MachoSymbols.Symbol> syms) {
        if (syms.Count == 0) return new Result(false, [], "no symbols");
        var wrappedRequests = new HashSet<string>(StringComparer.Ordinal);
        var wrappedResponses = new HashSet<string>(StringComparer.Ordinal);
        CollectWrapSignals(syms, wrappedRequests, wrappedResponses);

        var img = BinaryImage.Load(bin);
        var endpoints = new List<EndpointDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in syms) {
            if (s.Value == 0) continue;
            string mangled = StripLeadingUnderscore(s.Name);
            if (!mangled.StartsWith(MethodPrefix, StringComparison.Ordinal)) continue;
            if (!seen.Add(mangled)) continue;
            if (!TryParseMethod(mangled, out string method, out string? request, out string? response)) continue;

            string? path = FindPath(bin, img, syms, s.Name);
            if (path is null && request is null && response is null) continue;

            bool reqWrapped = request is not null && wrappedRequests.Contains(request);
            bool resWrapped = response is not null && wrappedResponses.Contains(response);
            endpoints.Add(new EndpointDescriptor(method, path, request, response, reqWrapped, resWrapped));
        }

        endpoints.Sort((a, b) => string.CompareOrdinal(a.Method, b.Method));
        return new Result(true, endpoints, $"{endpoints.Count} endpoints");
    }

    private static string StripLeadingUnderscore(string name) =>
        name.StartsWith("__Z", StringComparison.Ordinal) ? name[1..] : name;

    private static void CollectWrapSignals(IReadOnlyList<MachoSymbols.Symbol> syms, HashSet<string> requests,
        HashSet<string> responses) {
        foreach (var s in syms) {
            string n = s.Name;
            AddFromMarker(n, "create_authenticated_message", requests);
            AddFromMarker(n, "getAuthenticatedMessageData", requests);
            AddFromMarker(n, "rpc_auth", requests);
            AddFromMarker(n, "decode_authenticated_message", responses);
        }
    }

    private static void AddFromMarker(string name, string marker, HashSet<string> set) {
        int i = name.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return;
        if (TryReadEiType(name, FirstN(name, i + marker.Length), out string type, out _)) set.Add(type);
    }

    private static int FirstN(string s, int from) {
        for (int i = from; i < s.Length; i++) {
            if (s[i] == 'N') return i;
        }

        return s.Length;
    }

    private static bool TryParseMethod(string name, out string method, out string? request, out string? response) {
        method = "";
        request = null;
        response = null;
        string rest = name[MethodPrefix.Length..];
        int p = 0;
        while (p < rest.Length && char.IsAsciiDigit(rest[p])) p++;
        if (p == 0 || !int.TryParse(rest[..p], out int len) || len <= 0 || p + len > rest.Length) return false;

        method = rest[p..(p + len)];
        int after = p + len;
        if (after >= rest.Length || rest[after] != 'E' || !char.IsAsciiLetterLower(method[0])) return false;

        string args = rest[(after + 1)..];
        int fn = args.IndexOf("functionI", StringComparison.Ordinal);
        string reqScope = fn >= 0 ? args[..fn] : args;
        request = ReadFirstEiType(reqScope, 0);
        response = fn >= 0 ? ReadFirstEiType(args, fn) : null;
        return true;
    }

    private static string? ReadFirstEiType(string s, int from) {
        for (int i = from; i < s.Length; i++) {
            if (s[i] == 'N' && TryReadEiType(s, i, out string name, out _)) return name;
        }

        return null;
    }

    private static bool TryReadEiType(string s, int i, out string name, out int end) {
        name = "";
        end = i;
        if (i < 0 || i >= s.Length || s[i] != 'N') return false;
        int j = i + 1;
        if (j + 3 <= s.Length && s[j] == '2' && s[j + 1] == 'e' && s[j + 2] == 'i') {
            j += 3;
        } else if (j < s.Length && s[j] == 'S') {
            j++;
            while (j < s.Length && char.IsAsciiLetterOrDigit(s[j])) j++;
            if (j >= s.Length || s[j] != '_') return false;
            j++;
        } else {
            return false;
        }

        int lenStart = j;
        while (j < s.Length && char.IsAsciiDigit(s[j])) j++;
        if (j == lenStart || !int.TryParse(s[lenStart..j], out int len) || len <= 0 || j + len > s.Length) return false;

        string nm = s[j..(j + len)];
        int nameEnd = j + len;
        if (nameEnd >= s.Length || s[nameEnd] != 'E' || !IsTypeName(nm)) return false;

        name = nm;
        end = nameEnd + 1;
        return true;
    }

    private static bool IsTypeName(string s) {
        if (s.Length == 0 || !char.IsAsciiLetterUpper(s[0])) return false;
        foreach (char c in s) {
            if (!char.IsAsciiLetterOrDigit(c)) return false;
        }

        return true;
    }

    private static string? FindPath(byte[] bin, IBinaryImage? img, IReadOnlyList<MachoSymbols.Symbol> syms,
        string mangled) {
        var scan = Arm64DataTableReader.ScanWith(bin, syms, [mangled]);
        if (!scan.Ok) return null;

        string? prevStr = null;
        ulong prevVa = 0;
        foreach (var r in scan.Addresses) {
            if (!IsStringSection(r.Section)) continue;
            string str = BinaryStrings.ReadCstr(bin, img, r.Va, 128);
            if (string.IsNullOrEmpty(str) || !IsPrintable(str)) continue;

            if (prevStr is not null && prevStr.EndsWith(str, StringComparison.Ordinal)
                                    && r.Va > prevVa && r.Va <= prevVa + (ulong)prevStr.Length + 1) {
                continue;
            }

            prevStr = str;
            prevVa = r.Va;

            if (PathPattern().IsMatch(str)) return str;
        }

        return null;
    }

    private static bool IsStringSection(string name) =>
        name is "__cstring" or ".rodata" or ".data.rel.ro" or "__const";

    private static bool IsPrintable(string s) {
        if (s.Length < 2) return false;
        foreach (char c in s) {
            if (c is < (char)0x20 or > (char)0x7e) return false;
        }

        return true;
    }

    public sealed record EndpointDescriptor(
        string Method,
        string? Path,
        string? RequestType,
        string? ResponseType,
        bool RequestWrapped,
        bool ResponseWrapped);

    public sealed record Result(bool Ok, IReadOnlyList<EndpointDescriptor> Endpoints, string Diagnostics);
}
