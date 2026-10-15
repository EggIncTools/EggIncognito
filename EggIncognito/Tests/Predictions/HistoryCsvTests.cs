using System.Text;
using EggIncognito.Models.Contracts;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class HistoryCsvTests {
    private static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Contracts_PgAdminExport_ParsesRowsAndTimestamps() {
        const string csv = """
            "contract_id","name","egg","custom_egg_id","season_id","start_time","end_time","length_seconds","leggacy","ultra_only","prophecy_eggs","source"
            "engage","Engage!",11,NULL,NULL,"2018-03-29 02:13:37.737542+00","2018-03-31 02:13:37.737542+00",172800.0,False,False,1,"carpet"
            "easter-real","Easter Egg Shortage",101,NULL,NULL,"2018-04-01 16:33:15.093102+00","2018-04-08 16:33:15.093102+00",604800.0,False,False,2,"carpet"

            """;

        var rows = HistoryCsv.Contracts(Utf8(csv));

        Assert.Equal(2, rows.Count);
        var engage = rows[0];
        Assert.Equal("engage", engage.ContractId);
        Assert.Equal("Engage!", engage.Name);
        Assert.Equal(11, engage.Egg);
        Assert.Null(engage.CustomEggId);
        Assert.Equal(172800.0, engage.LengthSeconds);
        Assert.False(engage.Leggacy);
        Assert.False(engage.UltraOnly);
        Assert.Equal(1, engage.ProphecyEggs);
        Assert.Equal(ContractSlotKind.NewContract, engage.ReleaseKind);
        Assert.Equal(
            new DateTimeOffset(2018, 3, 29, 2, 13, 37, TimeSpan.Zero).ToUnixTimeSeconds(), (long)engage.Start);
    }

    [Fact]
    public void Contracts_CustomEggId_IsKeptAsColleggtibleId() {
        const string csv = """
            "contract_id","name","egg","custom_egg_id","season_id","start_time","end_time","length_seconds","leggacy","ultra_only","prophecy_eggs","source"
            "leggacy-pe","Leggacy PE",5,"shippy",NULL,"2022-01-07 12:00:00+00","2022-01-14 12:00:00+00",604800.0,True,False,1,"carpet"

            """;

        var rows = HistoryCsv.Contracts(Utf8(csv));

        var row = Assert.Single(rows);
        Assert.Equal("shippy", row.CustomEggId);
        Assert.True(row.Leggacy);
        Assert.Equal(ContractSlotKind.PeLeggacy, row.ReleaseKind);
    }

    [Fact]
    public void GameEvents_PgAdminExport_ParsesTypeAndWindow() {
        const string csv = """
            "event_id","event_type","message","multiplier","ultra","start_time","end_time","source"
            "covid-1-1-pres","prestige-boost","DOUBLE PRESTIGE!",2.0,False,"2021-01-01 16:08:04.875788+00","2021-01-02 17:08:04.875788+00","carpet"
            "covid-1-1","piggy-boost","TRIPLE PIGGY GROWTH!!",3.0,False,"2021-01-01 16:08:08.348588+00","2021-01-04 17:58:22.022415+00","carpet"

            """;

        var rows = HistoryCsv.GameEvents(Utf8(csv));

        Assert.Equal(2, rows.Count);
        Assert.Equal("prestige-boost", rows[0].Type);
        Assert.False(rows[0].Ultra);
        Assert.Equal(2.0, rows[0].Multiplier);
        Assert.True(rows[0].End > rows[0].Start);
    }

    [Fact]
    public void GameEvents_UltraTrue_ParsesAsUltra() {
        const string csv = """
            "event_id","event_type","message","multiplier","ultra","start_time","end_time","source"
            "ultra-1","earnings-boost","ULTRA!",5.0,True,"2022-06-01 00:00:00+00","2022-06-02 00:00:00+00","carpet"

            """;

        var rows = HistoryCsv.GameEvents(Utf8(csv));

        Assert.True(Assert.Single(rows).Ultra);
    }
}
