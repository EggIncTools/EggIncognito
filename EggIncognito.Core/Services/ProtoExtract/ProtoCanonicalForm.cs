using System.Diagnostics.CodeAnalysis;

namespace EggIncognito.Core.Services.ProtoExtract;

public static class ProtoCanonicalForm {
    public static NormalizeResult Normalize(string protoText) {
        var result = NormalizeOnce(protoText);
        if (result.Ok) return result;
        if (result.Error is { } err
            && (err.Contains("unresolved type 'aux.", StringComparison.Ordinal)
                || err.Contains("unresolved type '.aux.", StringComparison.Ordinal))) {
            var repaired = NormalizeOnce(ProtoCleanup.MergeLegacyCommon(protoText));
            if (repaired.Ok) return repaired;
        }

        return result;
    }

    private static NormalizeResult NormalizeOnce(string protoText) {
        try {
            var first = ProtoTextCompiler.Compile(protoText);
            string text1 = DescriptorProtoCarver.EmitProto(first);
            var second = ProtoTextCompiler.Compile(text1);
            string text2 = DescriptorProtoCarver.EmitProto(second);
            if (!string.Equals(text1, text2, StringComparison.Ordinal)) {
                return new NormalizeResult(false, null, null, "fixpoint mismatch");
            }

            return new NormalizeResult(true, text1, EggIncognito.Core.ProtoHash.OfDescriptor(second), null);
        } catch (FormatException ex) {
            return new NormalizeResult(false, null, null, ex.Message);
        }
    }

    public sealed record NormalizeResult(bool Ok, string? Text, string? Sha, string? Error) {
        [MemberNotNullWhen(true, nameof(Text), nameof(Sha))]
        public bool Ok { get; init; } = Ok;
    }
}
