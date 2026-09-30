namespace EggIncognito.Models.Protos;

public sealed record OfferItem(
    string Platform,
    string? AppVersion,
    string? Build,
    string? ClientVersion,
    string? Package,
    string? FileSha,
    string? ProtoText);
