namespace EggIncognito.Models.Admin;

public sealed record GameDataStatusResponse(
    List<GameDataDocRow> Documents,
    List<string> Missing,
    DataStatusConfig Config,
    List<DataStatusFixtureRow> Fixtures);
