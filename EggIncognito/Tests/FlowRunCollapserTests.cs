using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class FlowRunCollapserTests {
    private static DashboardFlow F(long id, string path = "ei_afx/craft_artifact", int status = 200,
        string? req = "CraftArtifactRequest", string? res = "CraftArtifactResponse") =>
        new(id, $"00:00:{id:00}", path, "POST", status, null, null, "AAEC", null, req, res);

    [Fact]
    public void BackToBackSameShapeFoldsIntoOneRun() {
        var runs = FlowRunCollapser.Fold([F(1), F(2), F(3), F(4)]);

        var only = Assert.Single(runs);
        Assert.Equal(4, only.Count);
        Assert.Equal(1L, only.Id);
        Assert.Equal(4L, only.Last.Id);
        Assert.True(only.Folded);
    }

    [Fact]
    public void ShortRunsStayAsSingleRows() {
        var runs = FlowRunCollapser.Fold([F(1), F(2)]);

        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.False(r.Folded));
    }

    [Fact]
    public void DifferentPathStatusOrTypeBreaksTheRun() {
        var runs = FlowRunCollapser.Fold([
            F(1), F(2), F(3),
            F(4, status: 500),
            F(5), F(6), F(7),
            F(8, path: "ei/first_contact"),
            F(9, res: "Other"), F(10), F(11), F(12)
        ]);

        Assert.Equal(6, runs.Count);
        Assert.Equal(3, runs[0].Count);
        Assert.Equal(1, runs[1].Count);
        Assert.Equal(3, runs[2].Count);
        Assert.Equal(1, runs[3].Count);
        Assert.Equal(1, runs[4].Count);
        Assert.Equal(3, runs[5].Count);
    }

    [Fact]
    public void RunStartOffsetsIndexTheSourceList() {
        var runs = FlowRunCollapser.Fold([F(1, path: "a"), F(2), F(3), F(4), F(5, path: "b")]);

        Assert.Equal(0, runs[0].Start);
        Assert.Equal(1, runs[1].Start);
        Assert.Equal(4, runs[2].Start);
    }

    [Fact]
    public void EmptyInputYieldsNoRuns() => Assert.Empty(FlowRunCollapser.Fold([]));
}
