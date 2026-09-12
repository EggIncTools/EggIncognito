using EggIncognito.Services.DataApi;

namespace EggIncognito.Models.Admin;

public sealed record DataStatusResponse(
    List<DataStatusGameDataRow> GameData,
    List<GameDataDocInfo> Documents,
    List<string> Missing,
    DataStatusConfig Config,
    List<DataStatusFixtureRow> Fixtures);
