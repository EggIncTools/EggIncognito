using EggIncognito.Models.Inspector;
using EggIncognito.Services.Api;
using EggIncognito.Services.Inspector;

namespace EggIncognito.Tests;

public class ApiWorkbenchDatasetHashTests {
    [Fact]
    public void ApplyHash_ReadsARootDataset() {
        var state = new ApiWorkbenchState();
        Assert.True(state.ApplyHash("#data/periodical/get_periodicals"));
        Assert.Equal("periodical", state.Group);
        Assert.Equal("get_periodicals", state.Id);
        Assert.Null(state.Sub);
    }

    [Fact]
    public void ApplyHash_ReadsAChildDataset() {
        var state = new ApiWorkbenchState();
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
    [InlineData("#ios_1140823...android_111358/split")]
    [InlineData("#data")]
    [InlineData("#data/")]
    [InlineData("#data/periodical")]
    [InlineData("#data/periodical/config/shells/extra")]
    [InlineData("#data//get_periodicals")]
    [InlineData("#api/keys")]
    [InlineData("#api/keys/all")]
    [InlineData("#api/docs/config/AppMode")]
    [InlineData("#api/docs/control/Foo")]
    public void ApplyHash_RejectsEverythingThatIsNotADataGrammar(string hash) => Assert.False(new ApiWorkbenchState().ApplyHash(hash));

    [Theory]
    [InlineData("#data/gamedata/mission.json")]
    [InlineData("#data/gamedata/mission.about")]
    [InlineData("#data/gamedata/mis.sion.about")]
    [InlineData("#data/game.data/mission")]
    [InlineData("#data/periodical/afx-config/eiafx.about")]
    public void ApplyHash_RejectsASegmentCarryingADot(string hash) => Assert.False(new ApiWorkbenchState().ApplyHash(hash));

    [Fact]
    public void Hash_IsApiWithoutASelection() => Assert.Equal("api", new ApiWorkbenchState().Hash());

    [Fact]
    public void Hash_RoundTripsARoot() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("gamedata", "mission", null);

        Assert.Equal("api/data/gamedata/mission", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("gamedata", fresh.Group);
        Assert.Equal("mission", fresh.Id);
        Assert.Null(fresh.Sub);
    }

    [Fact]
    public void Hash_RoundTripsAChild() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("periodical", "config", "shells");

        Assert.Equal("api/data/periodical/config/shells", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal("periodical", fresh.Group);
        Assert.Equal("config", fresh.Id);
        Assert.Equal("shells", fresh.Sub);
    }

    [Fact]
    public void Hash_RoundTripsTheCapturePane() {
        var state = new ApiWorkbenchState { Kind = ApiSelectionKind.Capture };

        Assert.Equal("api/capture", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(ApiSelectionKind.Capture, fresh.Kind);
    }

    [Fact]
    public void ApplyHash_LeavesTheStateAloneWhenItDoesNotMatch() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("gamedata", "mission", null);

        Assert.False(state.ApplyHash("#notify_7"));
        Assert.Equal("mission", state.Id);
    }

    [Fact]
    public void TheApiWorkbenchOffersFourModes() {
        var state = new ApiWorkbenchState();

        Assert.Equal(["docs", "apis", "data", "capture"], state.Modes.Select(m => m.Key));
        Assert.Equal(["Docs", "APIs", "Data", "Capture"], state.Modes.Select(m => m.Label));
        Assert.Equal("apis", state.DefaultMode);
    }

    [Theory]
    [InlineData(ApiSelectionKind.Endpoint, "apis")]
    [InlineData(ApiSelectionKind.Routes, "apis")]
    [InlineData(ApiSelectionKind.Dataset, "data")]
    [InlineData(ApiSelectionKind.Capture, "capture")]
    [InlineData(ApiSelectionKind.Docs, "docs")]
    public void ModeFor_MapsEveryKindToItsMode(ApiSelectionKind kind, string mode) => Assert.Equal(mode, ApiWorkbenchState.ModeFor(kind));

    [Fact]
    public void Hash_RoundTripsADocsSubject() {
        var state = new ApiWorkbenchState {
            Docs = new DocSubjectRef(DocSubjectKind.Message, "ContractsResponse"),
            Kind = ApiSelectionKind.Docs
        };

        Assert.Equal("api/docs/message/ContractsResponse", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(ApiSelectionKind.Docs, fresh.Kind);
        Assert.Equal(new DocSubjectRef(DocSubjectKind.Message, "ContractsResponse"), fresh.Docs);
    }

    [Fact]
    public void Hash_RoundTripsADottedMessageKey() {
        var state = new ApiWorkbenchState {
            Docs = new DocSubjectRef(DocSubjectKind.Message, "Backup.Game"),
            Kind = ApiSelectionKind.Docs
        };

        Assert.Equal("api/docs/message/Backup.Game", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(new DocSubjectRef(DocSubjectKind.Message, "Backup.Game"), fresh.Docs);
    }

    [Fact]
    public void Hash_RoundTripsADocsEndpointSubjectWithSlashes() {
        var state = new ApiWorkbenchState {
            Docs = new DocSubjectRef(DocSubjectKind.Endpoint, "ei/first_contact"),
            Kind = ApiSelectionKind.Docs
        };

        Assert.Equal("api/docs/endpoint/ei/first_contact", state.Hash());

        var fresh = new ApiWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(new DocSubjectRef(DocSubjectKind.Endpoint, "ei/first_contact"), fresh.Docs);
    }

    [Fact]
    public void ApplyHash_LeavesTheDocsSubjectUnselectedForTheBareDocsHash() {
        var state = new ApiWorkbenchState();

        Assert.True(state.ApplyHash("#api/docs"));
        Assert.Equal(ApiSelectionKind.Docs, state.Kind);
        Assert.Null(state.Docs);
        Assert.Equal("api/docs", state.Hash());
    }

    [Theory]
    [InlineData("#api/routes", "apis")]
    [InlineData("#data/periodical/config/shells", "data")]
    [InlineData("#api/capture", "capture")]
    [InlineData("#api/ep:ei/get_periodicals", "apis")]
    public void ApplyHash_DrivesTheModeFromTheKind(string hash, string mode) {
        var state = new ApiWorkbenchState();

        Assert.True(state.ApplyHash(hash));
        Assert.Equal(mode, state.Mode);
    }

    [Fact]
    public void SwitchingModes_RestoresTheRememberedSelection() {
        var state = new ApiWorkbenchState();
        state.SelectDataset("gamedata", "mission", "about");
        state.RememberSelection();

        state.Kind = ApiSelectionKind.Capture;
        Assert.Equal("capture", state.Mode);

        Assert.True(state.RestoreSelection("data"));
        Assert.Equal(ApiSelectionKind.Dataset, state.Kind);
        Assert.Equal("data", state.Mode);
        Assert.Equal("api/data/gamedata/mission/about", state.Hash());
    }

    [Fact]
    public void RestoreSelection_ReportsNoMemoryForAnUnvisitedMode() => Assert.False(new ApiWorkbenchState().RestoreSelection("data"));
}
