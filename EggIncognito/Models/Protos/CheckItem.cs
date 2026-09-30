namespace EggIncognito.Models.Protos;

public sealed record CheckItem(
    string ProtoSha,
    string? Platform,
    string? AppVersion,
    string? Build,
    string? ClientVersion);
