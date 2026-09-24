using System.Collections.Frozen;
using System.Reflection;
using Ei;

namespace EggIncognito.Core.Services;

public sealed class HarCounts {
    public int Wrote { get; set; }
    public int Upd { get; set; }
    public int Diff { get; set; }
    public int Same { get; set; }
    public int Err { get; set; }
    public int Loss { get; set; }

    public List<string> Learned { get; } = [];
    public List<string> Flagged { get; } = [];
    public bool WroteYaml { get; set; }
}

public sealed record HarDirs(
    string OutDir,
    string StagedDir,
    string RequestsDir,
    IReadOnlyDictionary<string, string> TypeMap,
    IReadOnlyDictionary<string, string> RequestTypeMap,
    HashSet<string> RequestWrapped,
    RoutesYamlEditor Yaml);

public enum AutoWriteVerdict {
    Reject,
    Flag,
    Write
}

public sealed record RequestDecode(string? Json, string? DetectedType, bool DetectedWrapped, string? FlagNote) {
    public bool EmptyBody { get; init; }
}

public sealed record DecodedEntry(
    string Path,
    string Json,
    RequestDecode Request,
    string? AutoResponseType,
    int RespBestScore,
    int RespSecondScore);

public static class ExtractorConfig {
    public const int ExactBonus = 1000;

    internal static readonly FrozenSet<string> AlwaysSkip =
        new[] { "ei/get_config" }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly Assembly EiAssembly = typeof(AuthenticatedMessage).Assembly;

    public static AutoWriteVerdict ClassifyAutoWrite(int bestScore, int secondBestScore) {
        if (bestScore < ExactBonus) return AutoWriteVerdict.Reject;
        if (secondBestScore < ExactBonus) return AutoWriteVerdict.Write;
        int bestFields = bestScore - ExactBonus;
        int secondFields = secondBestScore - ExactBonus;
        return bestFields > secondFields ? AutoWriteVerdict.Write : AutoWriteVerdict.Flag;
    }
}
