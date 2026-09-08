namespace EggIncognito.Models.Routes;

public sealed record RouteBinaryRow(
    string Path,
    string? Method,
    string? Request,
    string? Response,
    bool RequestWrapped,
    bool ResponseWrapped,
    string? BinaryVersion,
    string? Platform,
    DateTimeOffset RefreshedAt);
