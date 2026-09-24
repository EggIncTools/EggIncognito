using System.Text;
using EggIncognito.Core.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Capture;

public sealed class FlowDecoder {
#pragma warning disable IDE0028
    private readonly HashSet<string> _rawResponsePaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _requestTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requestWrapped = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _responseTypes = new(StringComparer.Ordinal);
#pragma warning restore IDE0028

    public FlowDecoder(string contentRoot) : this(RouteCatalog.ForRepo(contentRoot)) {
    }

    public FlowDecoder(IRouteCatalog catalog) {
        foreach (var route in catalog.All()) {
            if (route.RawResponse is not null) _rawResponsePaths.Add(route.Path);
            if (route.Request is not null) _requestTypes[route.Path] = route.Request;
            if (route.RequestWrapped) _requestWrapped.Add(route.Path);
            if (route.Response is not null) _responseTypes[route.Path] = route.Response;
        }
    }

    public string? KnownResponseType(string path) =>
        _responseTypes.GetValueOrDefault(path);

    public string? KnownRequestType(string path) =>
        _requestTypes.GetValueOrDefault(path);

    private static DecodeResult Result(string? rawJson, string? type, bool known) =>
        rawJson is null
            ? new DecodeResult(null, null, type, known)
            : new DecodeResult(Redactor.Redact(rawJson), rawJson, type, known);

    public DecodeResult DecodeResponse(string path, string responseB64) {
        byte[] respBytes;
        try {
            respBytes = ProtoFraming.FromBase64Loose(responseB64);
        } catch {
            return new DecodeResult(null, null, null, false);
        }

        string? knownType = KnownResponseType(path);

        byte[]? inner = TryUnwrapResponse(respBytes);

        if (knownType is not null) {
            var direct = ScoreKnown(knownType, respBytes);
            var wrapped = inner is { Length: > 0 } ? ScoreKnown(knownType, inner) : (score: 0, json: null);
            (_, string? json) = wrapped.score > direct.score ? wrapped : direct;
            if (json is not null)
                return Result(json, knownType, true);
        } else {
            if (inner is { Length: > 0 }) {
                (string? typeName, string? json, _, _, _) = EndpointExtractor.AutoDetect(inner);
                if (json is not null)
                    return Result(json, typeName, false);
            }

            var detRaw = EndpointExtractor.AutoDetect(respBytes);
            if (detRaw.Json is not null)
                return Result(detRaw.Json, detRaw.TypeName, false);
        }

        string? text = AsPrintableText(respBytes);
        bool isAck = _rawResponsePaths.Contains(path);
        return text is not null
            ? new DecodeResult(null, null, isAck ? "acknowledgement" : "text", isAck, isAck, text)
            : isAck
                ? new DecodeResult(null, null, "acknowledgement", true, true)
                : new DecodeResult(null, null, null, false);
    }

    private static byte[]? TryUnwrapResponse(byte[] respBytes) {
        try {
            var outer = AuthenticatedMessage.Parser.ParseFrom(respBytes);
            return outer.Compressed
                ? ProtoFraming.Decompress(outer.Message.ToByteArray())
                : outer.Message.ToByteArray();
        } catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidDataException) {
            return null;
        }
    }

    private static (int score, string? json) ScoreKnown(string knownType, byte[] data) {
        try {
            var msg = EndpointExtractor.ParseByTypeName(knownType, data);
            if (msg is null) return (0, null);
            string? json = JsonFormatter.Default.Format(msg);
            int fieldScore = json.Count(c => c == ':');
            bool exact = msg.ToByteArray().AsSpan().SequenceEqual(data);
            return (exact ? 1000 + fieldScore : fieldScore, ProtoJson.PrettyPrint(json));
        } catch {
            return (0, null);
        }
    }

    private static string? AsPrintableText(byte[] bytes) {
        if (bytes.Length is 0 or > 256) return null;
        foreach (byte b in bytes) {
            if (b is < 0x20 and not ((byte)'\t' or (byte)'\r' or (byte)'\n') or 0x7f)
                return null;
        }

        try {
            return Encoding.UTF8.GetString(bytes).Trim();
        } catch {
            return null;
        }
    }

    public DecodeResult DecodeRequest(string path, string? requestDataB64) {
        string? knownType = KnownRequestType(path);
        if (string.IsNullOrEmpty(requestDataB64)) return new DecodeResult(null, null, knownType, false);
        try {
            byte[] bytes = ProtoFraming.FromBase64Loose(requestDataB64);
            bool wrapped = _requestWrapped.Contains(path);
            (string? json, string? type) = EndpointExtractor.DecodeRequestBody(knownType, wrapped, bytes);
            return Result(json, type, knownType is not null && json is not null);
        } catch {
            return new DecodeResult(null, null, knownType, false);
        }
    }

    public sealed record DecodeResult(
        string? Json,
        string? JsonRaw,
        string? Type,
        bool Known,
        bool Ack = false,
        string? Text = null);
}
