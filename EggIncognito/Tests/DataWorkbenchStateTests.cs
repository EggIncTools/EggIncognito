using EggIncognito.Services.Api;

namespace EggIncognito.Tests;

public class DataWorkbenchStateTests {
    [Fact]
    public void ApplyHash_ReadsARootDataset() {
        var state = new DataWorkbenchState();
        Assert.True(state.ApplyHash("#data/periodical/get_periodicals"));
        Assert.Equal("periodical", state.Group);
        Assert.Equal("get_periodicals", state.Id);
        Assert.Null(state.Sub);
    }

    [Fact]
    public void ApplyHash_ReadsAChildDataset() {
        var state = new DataWorkbenchState();
        Assert.True(state.ApplyHash("#data/periodical/config/shells"));
        Assert.Equal("periodical", state.Group);
        Assert.Equal("config", state.Id);
        Assert.Equal("shells", state.Sub);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#notify")]
    [InlineData("#notify_7_history")]
    [InlineData("#android_111358")]
    [InlineData("#data/periodical")]
    [InlineData("#data/periodical/config/shells/extra")]
    [InlineData("#data//get_periodicals")]
    [InlineData("#data/keys")]
    [InlineData("#api/data/periodical/get_periodicals")]
    public void ApplyHash_RejectsEverythingThatIsNotADataGrammar(string hash) =>
        Assert.False(new DataWorkbenchState().ApplyHash(hash));

    [Theory]
    [InlineData("#data/gamedata/mission.json")]
    [InlineData("#data/gamedata/mission.about")]
    [InlineData("#data/gamedata/mis.sion.about")]
    [InlineData("#data/game.data/mission")]
    [InlineData("#data/periodical/afx-config/eiafx.about")]
    public void ApplyHash_RejectsASegmentCarryingADot(string hash) =>
        Assert.False(new DataWorkbenchState().ApplyHash(hash));

    [Fact]
    public void Hash_IsDataWithoutASelection() => Assert.Equal("data", new DataWorkbenchState().Hash());

    [Fact]
    public void Hash_RoundTripsARoot() {
        var state = new DataWorkbenchState();
        state.SelectDataset("gamedata", "mission", null);

        Assert.Equal("data/gamedata/mission", state.Hash());

        var fresh = new DataWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("gamedata", fresh.Group);
        Assert.Equal("mission", fresh.Id);
        Assert.Null(fresh.Sub);
    }

    [Fact]
    public void Hash_RoundTripsAChild() {
        var state = new DataWorkbenchState();
        state.SelectDataset("periodical", "config", "shells");

        Assert.Equal("data/periodical/config/shells", state.Hash());

        var fresh = new DataWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("periodical", fresh.Group);
        Assert.Equal("config", fresh.Id);
        Assert.Equal("shells", fresh.Sub);
    }

    [Fact]
    public void ApplyHash_ReadsCoverageAsConsumptionExplorer() {
        var state = new DataWorkbenchState();
        Assert.True(state.ApplyHash("#data/coverage"));
        Assert.Equal(DataPaneKind.ConsumptionExplorer, state.Kind);
        Assert.Equal("data/consumption", state.Hash());
    }

    [Fact]
    public void ApplyHash_LeavesTheStateAloneWhenItDoesNotMatch() {
        var state = new DataWorkbenchState();
        state.SelectDataset("gamedata", "mission", null);

        Assert.False(state.ApplyHash("#notify_7"));
        Assert.Equal("mission", state.Id);
    }
}
