namespace EggIncognito.Models.Admin;

public sealed record GameDataSourceRow(string Field, string Origin, string? Locator, string? Method);

public sealed record GameDataDocRow(
    string Id,
    bool Present,
    bool Unbuildable,
    DateTimeOffset? UpdatedAt,
    int? Bytes,
    int? Count,
    string? GameVersion,
    List<GameDataSourceRow> Sources);
