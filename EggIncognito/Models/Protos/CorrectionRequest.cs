namespace EggIncognito.Models.Protos;

public sealed record CorrectionRequest(
    string TargetPlatform,
    string TargetBuild,
    string Platform,
    string? AppVersion,
    string? Build,
    string? ClientVersion,
    string? Package,
    string ProtoSha,
    string ProtoText,
    string? MessageIndex);
