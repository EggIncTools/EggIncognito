namespace EggIncognito.Core.Services;

public static class BlobDecoder {
    public static DecodeResult Decode(string base64) {
        byte[] bytes;
        try {
            bytes = ProtoFraming.FromBase64Loose(base64);
        } catch {
            return new DecodeResult(null, null, false, 0);
        }

        var raw = EndpointExtractor.AutoDetect(bytes);
        byte[]? unwrappedBytes = ProtoFraming.TryUnwrap(bytes);
        var unw = unwrappedBytes is null
            ? default
            : EndpointExtractor.AutoDetect(unwrappedBytes);

        bool useUnwrapped = unwrappedBytes is not null && unw.BestScore > raw.BestScore;
        (string? typeName, string? json, int confidence, _, _) = useUnwrapped ? unw : raw;
        return typeName is null || json is null
            ? new DecodeResult(null, null, useUnwrapped, 0)
            : new DecodeResult(typeName, json, useUnwrapped, confidence);
    }

    public sealed record DecodeResult(string? Type, string? Json, bool Wrapped, int Confidence);
}
