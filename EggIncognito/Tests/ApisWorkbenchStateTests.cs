using EggIncognito.Models.Inspector;
using EggIncognito.Services.Api;
using EggIncognito.Services.Inspector;

namespace EggIncognito.Tests;

public class ApisWorkbenchStateTests {
    [Fact]
    public void Hash_IsApisWithoutASelection() => Assert.Equal("apis", new ApisWorkbenchState().Hash());

    [Theory]
    [InlineData("")]
    [InlineData("#notify")]
    [InlineData("#notify_7_history")]
    [InlineData("#data/periodical/config/shells")]
    [InlineData("#api/docs/config/AppMode")]
    public void ApplyHash_RejectsEverythingOutsideTheApisNamespace(string hash) =>
        Assert.False(new ApisWorkbenchState().ApplyHash(hash));

    [Fact]
    public void ApplyHash_ReadsRoutes() {
        var state = new ApisWorkbenchState();
        Assert.True(state.ApplyHash("#apis/routes"));
        Assert.Equal(ApisPaneKind.Routes, state.Kind);
        Assert.Equal("apis/routes", state.Hash());
    }

    [Fact]
    public void ApplyHash_LeavesTheDocsSubjectUnselectedForTheBareDocsHash() {
        var state = new ApisWorkbenchState();

        Assert.True(state.ApplyHash("#apis/docs"));
        Assert.True(state.DocsOpen);
        Assert.Null(state.Docs);
        Assert.Equal("apis/docs", state.Hash());
    }

    [Fact]
    public void Hash_RoundTripsADocsSubject() {
        var state = new ApisWorkbenchState {
            Docs = new DocSubjectRef(DocSubjectKind.Message, "ContractsResponse"),
            DocsOpen = true
        };

        Assert.Equal("apis/docs/message/ContractsResponse", state.Hash());

        var fresh = new ApisWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.True(fresh.DocsOpen);
        Assert.Equal(new DocSubjectRef(DocSubjectKind.Message, "ContractsResponse"), fresh.Docs);
    }

    [Fact]
    public void Hash_RoundTripsADottedMessageKey() {
        var state = new ApisWorkbenchState {
            Docs = new DocSubjectRef(DocSubjectKind.Message, "Backup.Game"),
            DocsOpen = true
        };

        Assert.Equal("apis/docs/message/Backup.Game", state.Hash());

        var fresh = new ApisWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(new DocSubjectRef(DocSubjectKind.Message, "Backup.Game"), fresh.Docs);
    }

    [Fact]
    public void Hash_RoundTripsADocsEndpointSubjectWithSlashes() {
        var state = new ApisWorkbenchState {
            Docs = new DocSubjectRef(DocSubjectKind.Endpoint, "ei/first_contact"),
            DocsOpen = true
        };

        Assert.Equal("apis/docs/endpoint/ei/first_contact", state.Hash());

        var fresh = new ApisWorkbenchState();
        Assert.True(fresh.ApplyHash(state.Hash()));
        Assert.Equal(new DocSubjectRef(DocSubjectKind.Endpoint, "ei/first_contact"), fresh.Docs);
    }

    [Fact]
    public void ApplyHash_LeavesTheStateAloneWhenItDoesNotMatch() {
        var state = new ApisWorkbenchState();
        state.ApplyHash("#apis/routes");

        Assert.False(state.ApplyHash("#notify_7"));
        Assert.Equal(ApisPaneKind.Routes, state.Kind);
    }
}
