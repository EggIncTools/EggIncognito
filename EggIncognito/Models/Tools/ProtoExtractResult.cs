namespace EggIncognito.Models.Tools;

public sealed record ProtoExtractResult(
    bool Ok,
    string? Proto,
    string Diagnostics,
    string? ProtoSha,
    IReadOnlyList<string> Messages,
    string? AppVersion = null,
    string? Build = null,
    int? ClientVersion = null,
    string? FileSha = null);
