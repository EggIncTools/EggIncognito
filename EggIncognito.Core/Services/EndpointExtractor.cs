using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Core.Services;

public sealed partial class EndpointExtractor(HarDirs dirs, string? eid, string eidPlaceholder, bool overwrite) {
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly HashSet<string> _reqErrorSeen = [with(StringComparer.Ordinal)];

    private readonly HashSet<string> _seen = [with(StringComparer.Ordinal)];

    public HarCounts Counts { get; } = new();

    public bool Quiet { get; set; }

    public IReadOnlySet<string> LiveRoutes { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public bool LiveOnly { get; set; }

    public IEndpointWriteObserver? WriteObserver { get; set; }

    private void Out(string line) {
        if (!Quiet) Console.WriteLine(line);
    }

    private void Err(string line) {
        if (!Quiet) Console.Error.WriteLine(line);
    }

    public static EndpointExtractor ForRepo(string contentRoot, string? eid, string eidPlaceholder, bool overwrite) {
        string endpointsRoot = Path.Combine(contentRoot, "Endpoints");
        string outDir = Path.Combine(endpointsRoot, "default");
        string stagedDir = Path.Combine(endpointsRoot, "staged");
        string requestsDir = Path.Combine(endpointsRoot, "requests");
        Directory.CreateDirectory(outDir);

        var dirs = new HarDirs(outDir, stagedDir, requestsDir,
            LoadEndpointTypes(contentRoot), LoadRequestTypes(contentRoot), LoadRequestWrapped(contentRoot),
            new RoutesYamlEditor(contentRoot));
        return new EndpointExtractor(dirs, eid, eidPlaceholder, overwrite);
    }

    public string? ProcessFlow(string url, string method, int status, string? requestDataB64, string responseBodyB64) {
        if (method != "POST") return null;
        if (status != 200) return null;

        string path = NormalizePath(url);
        bool live = LiveRoutes.Contains(path);
        if (LiveOnly && !live) return null;

        DecodedEntry? decoded;
        try {
            decoded = TryDecode(path, requestDataB64, responseBodyB64);
        } catch (Exception ex) {
            Err($"  ERR   {path}: {ex.Message}");
            Counts.Err++;
            return null;
        }

        if (decoded is null) return null;
        if (live) {
            WriteDecoded(decoded, true);
            return decoded.Path;
        }

        if (!_seen.Add(decoded.Path)) return null;

        WriteDecoded(decoded);
        return decoded.Path;
    }

    public string? ForceWriteEndpoint(string url, string method, int status, string? requestDataB64,
        string responseBodyB64) {
        if (method != "POST" || status != 200) return null;
        string path = NormalizePath(url);

        DecodedEntry? decoded;
        try {
            decoded = TryDecode(path, requestDataB64, responseBodyB64, true);
        } catch (Exception ex) {
            Err($"  ERR   {path}: {ex.Message}");
            Counts.Err++;
            return null;
        }

        if (decoded is null) return null;
        if (ExtractorConfig.AlwaysSkip.Contains(decoded.Path)) return null;

        WriteDecoded(decoded, true, true);
        return decoded.Path;
    }

    public void RunFromHar(string harPath) {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(harPath));
        var entries = doc.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray();
        foreach (var entry in entries)
            ProcessHarEntry(entry);
    }

    public void RunFromMitm(string mitmPath) {
        foreach (var f in MitmFlowReader.Read(File.ReadAllBytes(mitmPath)))
            ProcessFlow(f.Url, f.Method, f.Status, f.RequestDataB64, f.ResponseBodyB64);
    }

    public void ProcessHarEntry(JsonElement entry) {
        var req = entry.GetProperty("request");
        var res = entry.GetProperty("response");

        string method = req.GetProperty("method").GetString() ?? "";
        int status = res.GetProperty("status").GetInt32();
        string url = req.GetProperty("url").GetString()!;

        if (method != "POST" || status != 200) return;

        var contentEl = res.GetProperty("content");
        string rawText = contentEl.GetProperty("text").GetString()!;
        string responseBodyB64 = contentEl.TryGetProperty("encoding", out var enc) && enc.GetString() == "base64"
            ? Encoding.UTF8.GetString(Convert.FromBase64String(rawText)).Trim()
            : rawText.Trim();

        string? requestData = ReadRequestData(req);

        ProcessFlow(url, method, status, requestData, responseBodyB64);
    }

    public void Save() => dirs.Yaml.Save();

    private DecodedEntry? TryDecode(string path, string? requestDataB64, string responseBodyB64,
        bool isExplicit = false) {
        byte[] respBytes;
        try {
            respBytes = ProtoFraming.FromBase64Loose(responseBodyB64);
        } catch (FormatException) {
            return null;
        }

        var outer = AuthenticatedMessage.Parser.ParseFrom(respBytes);
        byte[]? inner = outer.Compressed
            ? ProtoFraming.Decompress(outer.Message.ToByteArray())
            : outer.Message.ToByteArray();

        var request = ExtractRequestJson(requestDataB64, path, isExplicit);

        string Scrub(string s) {
            return ScrubEid(s, eid, eidPlaceholder);
        }

        string? json = FormatResponse(path, inner, dirs.TypeMap);
        if (json is null) {
            var det = AutoDetect(inner);
            if (det.TypeName is null || det.Json is null) return null;
            string red = Scrub(Redactor.Redact(det.Json));
            return new DecodedEntry(path, red, request, det.TypeName, det.BestScore, det.SecondBestScore);
        }

        return new DecodedEntry(path, Scrub(json), request, null, 0, 0);
    }

    private void WriteDecoded(DecodedEntry decoded, bool forceOverwrite = false, bool isExplicit = false) {
        (string path, string json, var request, string? autoResponseType, int respBest, int respSecond) = decoded;
        string slug = path;
        if (ExtractorConfig.AlwaysSkip.Contains(slug)) return;

        json = PeriodicalsSanitizer.ScrubPlayerScope(
            dirs.TypeMap.TryGetValue(path, out string? knownType) ? knownType : autoResponseType, json);

        SelfRepair(path, request, autoResponseType, respBest, respSecond, isExplicit);

        string outFile = Path.Combine(dirs.OutDir, EndpointFile(slug));
        string? existing = File.Exists(outFile) ? File.ReadAllText(outFile, Encoding.UTF8) : null;
        if (!forceOverwrite && existing is not null && CountJsonFields(json) < CountJsonFields(existing)) {
            Counts.Loss++;
            Out($"  loss  {slug}.json  (skipped - fewer fields than existing)");
            return;
        }

        string writeResult;
        try {
            writeResult = WriteEndpointFile(outFile, json, overwrite || forceOverwrite, existing,
                LiveRoutes.Contains(path));
        } catch (Exception ex) when (LiveRoutes.Contains(path) &&
                                     ex is IOException or UnauthorizedAccessException) {
            Err($"  ERR   {slug}.json fixture write failed: {ex.Message}");
            writeResult = existing is not null && Comparable(existing, true) == Comparable(json, true)
                ? "same"
                : "upd";
        }
        if (writeResult is "wrote" or "upd") WriteObserver?.OnEndpointWritten(path, json, existing);
        switch (writeResult) {
            case "wrote":
                Counts.Wrote++;
                Out($"  wrote {slug}.json");
                break;
            case "upd":
                Counts.Upd++;
                Out($"  upd   {slug}.json");
                break;
            case "same": Counts.Same++; break;
            case "diff":
                Counts.Diff++;
                string stagedFile = Path.Combine(dirs.StagedDir, EndpointFile(slug));
                Directory.CreateDirectory(Path.GetDirectoryName(stagedFile)!);
                File.WriteAllText(stagedFile, json, Utf8NoBom);
                Out($"  diff  {slug}.json");
                Out(
                    $"        code-insiders --diff \"{Path.Combine(dirs.OutDir, EndpointFile(slug))}\" \"{stagedFile}\"");
                break;
        }

        if (request.Json is not null) {
            string reqFile = Path.Combine(dirs.RequestsDir, slug + ".request.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reqFile)!);
            if (!File.Exists(reqFile)) {
                string scrubbed = ScrubEid(Redactor.Redact(request.Json), eid, eidPlaceholder);
                File.WriteAllText(reqFile, scrubbed, Utf8NoBom);
                Out($"  req   {slug}.request.json  (wrote)");
            }
        }
    }

    private void SelfRepair(string capturedPath, RequestDecode request, string? autoResponseType,
        int respBest, int respSecond, bool isExplicit = false) {
        var yaml = dirs.Yaml;

        string path = yaml.CanonicalPath(capturedPath);

        if (request.DetectedType is not null && yaml.RequestUnresolved(path)) {
            if (yaml.SetFieldIfEmpty(path, "request", request.DetectedType)) {
                if (request.DetectedWrapped) yaml.SetWrappedFlag(path, "requestWrapped");
                Counts.Learned.Add(
                    $"{path}  request = {request.DetectedType}{(request.DetectedWrapped ? " (wrapped)" : "")}");
                Counts.WroteYaml = true;
            }
        } else if (request.EmptyBody && yaml.RequestUnresolved(path)) {
            if (yaml.MarkRequestNone(path)) {
                Counts.Learned.Add($"{path}  request = none (empty body observed)");
                Counts.WroteYaml = true;
            }
        }

        if (request.FlagNote is not null) Counts.Flagged.Add(request.FlagNote);

        if (autoResponseType is not null) {
            var verdict = ExtractorConfig.ClassifyAutoWrite(respBest, respSecond);

            if (verdict == AutoWriteVerdict.Write || isExplicit) {
                if (!yaml.HasPath(path)) {
                    if (yaml.AddRoute(path, request.DetectedType, request.DetectedWrapped, autoResponseType, true)) {
                        Counts.Learned.Add($"{path}  added -> response = {autoResponseType} (wrapped)");
                        Counts.WroteYaml = true;
                    }
                } else if (yaml.ResponseUnresolved(path) && yaml.SetFieldIfEmpty(path, "response", autoResponseType)) {
                    yaml.SetWrappedFlag(path, "responseWrapped");
                    Counts.Learned.Add($"{path}  response = {autoResponseType} (wrapped)");
                    Counts.WroteYaml = true;
                }
            } else if (verdict == AutoWriteVerdict.Flag) {
                Counts.Flagged.Add($"{path} response: {autoResponseType} tied on fields - verify with --decode");
            }
        }

        if (!yaml.RequestUnresolved(path) && !yaml.ResponseUnresolved(path)) {
            if (yaml.RemoveFromNeedsCapture(path))
                Counts.WroteYaml = true;
        }
    }

    private RequestDecode ExtractRequestJson(string? dataValue, string path, bool isExplicit = false) {
        try {
            if (string.IsNullOrEmpty(dataValue))

                return new RequestDecode(null, null, false, null) { EmptyBody = true };

            byte[] reqBytes = ProtoFraming.FromBase64Loose(dataValue);

            if (dirs.RequestTypeMap.TryGetValue(path, out string? typeName)) {
                byte[] toParse = dirs.RequestWrapped.Contains(path) ? ProtoFraming.Unwrap(reqBytes) : reqBytes;
                var msg = ParseByTypeName(typeName, toParse);
                if (msg is null) {
                    if (_reqErrorSeen.Add(path))
                        Err($"  req   {path}: no parser for requestType '{typeName}'");
                    return new RequestDecode(null, null, false, null);
                }

                return new RequestDecode(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)), null,
                    dirs.RequestWrapped.Contains(path), null);
            }

            (var chosen, bool useUnwrapped) = BestFraming(reqBytes);
            if (chosen.TypeName is null || chosen.Json is null) return new RequestDecode(null, null, false, null);

            var verdict = ExtractorConfig.ClassifyAutoWrite(chosen.BestScore, chosen.SecondBestScore);
            Out(
                $"  reqauto {path}  request -> {chosen.TypeName} ({(useUnwrapped ? "wrapped" : "raw")}, {verdict}, conf {chosen.Confidence}%)");

            if (verdict == AutoWriteVerdict.Write || (isExplicit && chosen.TypeName is not null))
                return new RequestDecode(chosen.Json, chosen.TypeName, useUnwrapped, null);

            string? note = verdict == AutoWriteVerdict.Flag
                ? $"{path} request: {chosen.TypeName} vs runner-up tied on fields - verify with --decode"
                : null;
            return new RequestDecode(chosen.Json, null, useUnwrapped, note);
        } catch (InvalidProtocolBufferException ex) when (ex.Message.Contains("ended unexpectedly")) {
            if (_reqErrorSeen.Add(path))
                Err($"  req   {path}: truncated (HAR capture limit)");
            return new RequestDecode(null, null, false, null);
        } catch (Exception ex) {
            if (_reqErrorSeen.Add(path))
                Err($"  req   {path}: {ex.GetType().Name}: {ex.Message}");
            return new RequestDecode(null, null, false, null);
        }
    }

    public void PrintSelfRepairReport() {
        if (Counts.Learned.Count == 0 && Counts.Flagged.Count == 0) return;

        Console.WriteLine();
        Console.WriteLine("Self-repair summary");
        if (Counts.Learned.Count > 0) {
            Console.WriteLine($"  learned ({Counts.Learned.Count}):");
            foreach (string l in Counts.Learned) Console.WriteLine($"    {l}");
        }

        if (Counts.Flagged.Count > 0) {
            Console.WriteLine($"  flagged for review ({Counts.Flagged.Count}):");
            foreach (string f in Counts.Flagged) Console.WriteLine($"    {f}");
        }
    }

    public static string? ReadRequestData(JsonElement reqEl) {
        if (!reqEl.TryGetProperty("postData", out var postData)) return null;
        if (postData.TryGetProperty("params", out var parms)) {
            foreach (var p in parms.EnumerateArray()) {
                if (p.TryGetProperty("name", out var name) && name.GetString() == "data")

                    return p.GetProperty("value").GetString()?.Replace(' ', '+');
            }
        }

        if (postData.TryGetProperty("text", out var text)) {
            foreach (string pair in (text.GetString() ?? "").Split('&')) {
                int eq = pair.IndexOf('=');
                if (eq < 0 || pair[..eq] != "data") continue;
                return Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", "%2B"));
            }
        }

        return null;
    }

    public static string ScrubEid(string text, string? eid, string placeholder) =>
        string.IsNullOrEmpty(eid)
            ? text
            : Regex.Replace(text, Regex.Escape(eid), _ => placeholder, RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(2));

    private static string EndpointFile(string slug) => slug + ".json";

    private static string WriteEndpointFile(string path, string json, bool overwrite, string? existing, bool live) {
        if (existing is null) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json, Utf8NoBom);
            return "wrote";
        }

        if (Comparable(existing, live) == Comparable(json, live)) return "same";
        if (!overwrite) return "diff";
        File.WriteAllText(path, json, Utf8NoBom);
        return "upd";
    }

    private static string Comparable(string json, bool live) {
        string trimmed = json.Trim();
        return ProtoJson.NormalizeFloats(live ? ProtoJson.StripVolatile(trimmed) : trimmed);
    }

    public static string NormalizePath(string url) {
        string path = new Uri(url).AbsolutePath.TrimStart('/');
        return EidSuffixRegex().Replace(path, "");
    }

    public static string? FormatResponse(string path, byte[] inner, IReadOnlyDictionary<string, string> typeMap) {
        if (!typeMap.TryGetValue(path, out string? typeName)) return null;
        try {
            var msg = ParseByTypeName(typeName, inner);
            return msg is null ? null : Redactor.Redact(ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg)));
        } catch {
            return null;
        }
    }

    public static IMessage? ParseByTypeName(string typeName, byte[] data) {
        var type = ExtractorConfig.EiAssembly.GetType($"Ei.{typeName}");
        var parser = type?.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) as MessageParser;
        return parser?.ParseFrom(data);
    }

    public static IReadOnlyDictionary<string, string> LoadEndpointTypes(string contentRoot) =>
        RouteTypeMap(contentRoot, r => r.Response);

    public static IReadOnlyDictionary<string, string> LoadRequestTypes(string contentRoot) =>
        RouteTypeMap(contentRoot, r => r.Request);

    public static IReadOnlyDictionary<string, string> LoadRawResponses(string contentRoot) =>
        RouteTypeMap(contentRoot, r => r.RawResponse);

    public static HashSet<string> LoadRequestWrapped(string contentRoot) {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in LoadRoutes(contentRoot)) {
            if (r.RequestWrapped) result.Add(r.Path);
        }

        return result;
    }

    private static Dictionary<string, string> RouteTypeMap(string contentRoot, Func<RouteInfo, string?> value) {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in LoadRoutes(contentRoot)) {
            if (value(r) is { Length: > 0 } v) result[r.Path] = v;
        }

        return result;
    }

    private static IReadOnlyList<RouteInfo> LoadRoutes(string contentRoot) =>
        RouteCatalog.ForRepo(contentRoot).All();

    internal static int CountJsonFields(string json) {
        int count = 0;
        bool inString = false, escape = false;
        foreach (char c in json) {
            if (escape) {
                escape = false;
                continue;
            }

            if (inString) {
                if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"') inString = true;
            else if (c == ':') count++;
        }

        return count;
    }

    public static void RunDecode(string base64) {
        byte[] data;
        try {
            data = ProtoFraming.FromBase64Loose(base64);
        } catch (Exception ex) {
            Console.Error.WriteLine($"not valid base64: {ex.Message}");
            return;
        }

        void Report(string label, byte[] bytes) {
            Console.WriteLine($"=== {label} ({bytes.Length} bytes) ===");
            var ranked = ExtractorConfig.EiAssembly.GetTypes()
                .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t))
                .Select(t => (name: t.Name, result: TryParseAs(t, bytes)))
                .Where(x => x.result.score > 0)
                .OrderByDescending(x => x.result.score)
                .Take(5)
                .ToList();

            if (ranked.Count == 0) {
                Console.WriteLine("  (no Ei.* type parses these bytes)");
                return;
            }

            foreach ((string name, (int score, string? _)) in ranked) {
                bool exact = score >= 1000;
                Console.WriteLine($"  {name,-32} score={score}{(exact ? "  [EXACT round-trip]" : "")}");
            }

            var top = ranked[0];
            Console.WriteLine();
            Console.WriteLine($"  best: Ei.{top.name}");
            Console.WriteLine(top.result.json);
            Console.WriteLine();
        }

        Report("raw bytes", data);

        try {
            var outer = AuthenticatedMessage.Parser.ParseFrom(data);
            if (outer.Message.Length > 0) {
                byte[]? inner = outer.Compressed
                    ? ProtoFraming.Decompress(outer.Message.ToByteArray())
                    : outer.Message.ToByteArray();
                Report($"unwrapped AuthenticatedMessage.message (compressed={outer.Compressed})", inner);
            }
        } catch (InvalidProtocolBufferException) {
            Console.WriteLine("  (not a wrapped AuthenticatedMessage)");
        }
    }

    public static (string? json, string? typeName) DecodeRequestBody(string? knownType, bool wrapped, byte[] bytes) {
        try {
            if (knownType is not null) {
                var candidates = new List<byte[]>();
                byte[]? unwrapped = ProtoFraming.TryUnwrap(bytes);
                if (wrapped && unwrapped is not null) {
                    candidates.Add(unwrapped);
                    candidates.Add(bytes);
                } else {
                    candidates.Add(bytes);
                    if (unwrapped is not null) candidates.Add(unwrapped);
                }

                IMessage? best = null;
                int bestScore = int.MinValue;
                foreach (byte[] cand in candidates) {
                    IMessage? m;

                    try {
                        m = ParseByTypeName(knownType, cand);
                    } catch (InvalidProtocolBufferException) {
                        continue;
                    }

                    if (m is null) continue;
                    string? json = JsonFormatter.Default.Format(m);
                    bool exact = m.ToByteArray().AsSpan().SequenceEqual(cand);

                    int score = (exact ? 100_000 : 0) + json.Count(c => c == ':');
                    if (score > bestScore) {
                        bestScore = score;
                        best = m;
                    }
                }

                if (best is not null)
                    return (ProtoJson.PrettyPrint(JsonFormatter.Default.Format(best)), knownType);
            }

            var (chosen, _) = BestFraming(bytes);
            return chosen.Json is null ? (null, null) : (chosen.Json, chosen.TypeName);
        } catch {
            return (null, null);
        }
    }

    private static (AutoDetectResult result, bool unwrapped) BestFraming(byte[] bytes) {
        var raw = AutoDetect(bytes);
        byte[]? unwrappedBytes = ProtoFraming.TryUnwrap(bytes);
        var unw = unwrappedBytes is null ? default : AutoDetect(unwrappedBytes);
        return unwrappedBytes is not null && unw.BestScore > raw.BestScore ? (unw, true) : (raw, false);
    }

    public readonly record struct AutoDetectResult(string? TypeName, string? Json, int Confidence, int BestScore,
        int SecondBestScore);

    public static AutoDetectResult AutoDetect(byte[] data) {
        string? bestType = null;
        string? bestJson = null;
        int bestScore = 0;
        int secondBestScore = 0;

        foreach (var type in ExtractorConfig.EiAssembly.GetTypes()
                     .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t))) {
            (int score, string? json) = TryParseAs(type, data);
            if (score > bestScore) {
                secondBestScore = bestScore;
                bestScore = score;
                bestType = type.Name;
                bestJson = json;
            } else if (score > secondBestScore) {
                secondBestScore = score;
            }
        }

        if (bestScore < 2) return new AutoDetectResult(null, null, 0, bestScore, secondBestScore);

        const int Exact = 1000;
        int confidence;
        if (secondBestScore == 0) {
            confidence = 100;
        } else if (bestScore >= Exact && secondBestScore < Exact) {
            confidence = 99;
        } else if (bestScore >= Exact && secondBestScore >= Exact) {
            int bf = bestScore - Exact, sf = secondBestScore - Exact;
            confidence = bf + sf == 0 ? 50 : Math.Min(99, (int)((double)bf / (bf + sf) * 100));
        } else {
            confidence = Math.Min(99, (int)((double)bestScore / (bestScore + secondBestScore) * 100));
        }

        return new AutoDetectResult(bestType, bestJson is null ? null : ProtoJson.PrettyPrint(bestJson), confidence,
            bestScore, secondBestScore);
    }

    public static (int score, string? json) TryParseAs(Type type, byte[] data) {
        try {
            if (type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) is not MessageParser parser) {
                return (0, null);
            }

            var msg = parser.ParseFrom(data);
            string? json = JsonFormatter.Default.Format(msg);
            int fieldScore = json.Count(c => c == ':');

            bool exact = msg.ToByteArray().AsSpan().SequenceEqual(data);
            return (exact ? 1000 + fieldScore : fieldScore, json);
        } catch (InvalidProtocolBufferException) {
            return (0, null);
        }
    }

    [GeneratedRegex(@"/EI\d+.*$")]
    private static partial Regex EidSuffixRegex();
}
