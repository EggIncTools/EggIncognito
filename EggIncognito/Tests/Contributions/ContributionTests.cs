using EggIncognito.Capture;
using EggIncognito.Data.Models;

namespace EggIncognito.Tests.Contributions;

public class ContributionTests {
    private const string Eid = "EI1234567890123456";

    [Fact]
    public void Project_LeavesContributionRoutesUntouched() {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "ei_afx/craft_artifact" };
        var flow = Flow("ei_afx/craft_artifact");

        var projected = LimitedFlowProjector.Project(flow, allowed);

        Assert.Same(flow, projected);
    }

    [Fact]
    public void Project_StripsEveryPayloadFieldFromOtherRoutes() {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "ei_afx/craft_artifact" };
        var flow = Flow("ei/first_contact");

        var projected = LimitedFlowProjector.Project(flow, allowed);

        Assert.Null(projected.RequestJson);
        Assert.Null(projected.ResponseJson);
        Assert.Null(projected.RequestJsonRaw);
        Assert.Null(projected.ResponseJsonRaw);
        Assert.Null(projected.RequestDataB64);
        Assert.Null(projected.ResponseText);
        Assert.Null(projected.RequestHeaders);
        Assert.Null(projected.ResponseHeaders);
        Assert.Null(projected.RequestHeadersRaw);
        Assert.Null(projected.ResponseHeadersRaw);
        Assert.Equal("", projected.ResponseB64);
        Assert.Equal("", projected.Url);
    }

    [Fact]
    public void Project_KeepsOnlyWhatTheRowNeedsToRender() {
        var flow = Flow("ei/first_contact");

        var projected = LimitedFlowProjector.Project(flow, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(flow.Id, projected.Id);
        Assert.Equal(flow.Timestamp, projected.Timestamp);
        Assert.Equal("ei/first_contact", projected.Path);
        Assert.Equal("POST", projected.Method);
        Assert.Equal(200, projected.Status);
        Assert.Equal("FirstContactRequest", projected.RequestType);
        Assert.Equal("EggIncFirstContactResponse", projected.ResponseType);
    }

    [Fact]
    public void Kinds_RegistryMapsRoutesToTheirKind() {
        var kinds = new CaptureContributionKinds([new StubKind()]);

        Assert.Equal("stub", Assert.Single(kinds.KindNames));
        Assert.Equal(["ei/one", "ei/two"], kinds.AllRoutes.Order());
        Assert.NotNull(kinds.For("ei/two"));
        Assert.Null(kinds.For("ei/get_periodicals"));
    }

    [Fact]
    public void Status_KnowsOnlyTheFourStates() {
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Recorded));
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Submitted));
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Approved));
        Assert.True(ContributedCaptureStatus.IsKnown(ContributedCaptureStatus.Rejected));
        Assert.False(ContributedCaptureStatus.IsKnown("published"));
        Assert.False(ContributedCaptureStatus.IsKnown(null));
    }

    private static DashboardFlow Flow(string path) =>
        new(7, "12:00:00", path, "POST", 200,
            "{\"rinfo\":{\"eiUserId\":\"" + Eid + "\"}}", "{\"secret\":true}", "AAEC", "BBBB",
            RequestType: "FirstContactRequest",
            ResponseType: "EggIncFirstContactResponse",
            RequestJsonRaw: "{\"rinfo\":{\"eiUserId\":\"" + Eid + "\"}}",
            ResponseJsonRaw: "{\"secret\":true}",
            Url: "https://auxbrain.com/" + path,
            ResponseText: "raw");

    private sealed class StubKind : ICaptureContributionKind {
        public ContributionDraft? Build(DashboardFlow flow) => null;
        public string Kind => "stub";
        public IReadOnlyCollection<string> Routes => ["ei/one", "ei/two"];
    }
}
