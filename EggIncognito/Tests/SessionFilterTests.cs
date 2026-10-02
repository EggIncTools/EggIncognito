using EggIncognito.Services.Filtering;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class SessionFilterTests {
    private static StagedEntry Entry(string status = "done", bool ok = true, string file = "egginc-1.36.0.apk",
        string platform = "android", string? fileSha = "f00dfeed00112233", string? protoSha = "abcdef0123456789") =>
        new() {
            Token = 1,
            FileName = file,
            Size = 30 * 1024 * 1024,
            Status = status,
            Platform = platform,
            AppVersion = "1.36.0",
            Build = "1.36.0.2",
            ClientVersionText = "74",
            Result = status == "done" ? new ExtractResult { Ok = ok, FileSha = fileSha, ProtoSha = protoSha } : null
        };

    [Fact]
    public void StateBucketsQueuedRunningFailedAndAnalysed() {
        Assert.Equal(SessionFilter.Pending, SessionFilter.StateOf(Entry("queued")));
        Assert.Equal(SessionFilter.Pending, SessionFilter.StateOf(Entry("extracting")));
        Assert.Equal(SessionFilter.Failed, SessionFilter.StateOf(Entry("error")));
        Assert.Equal(SessionFilter.Failed, SessionFilter.StateOf(Entry("done", ok: false)));
        Assert.Equal(SessionFilter.Analysed, SessionFilter.StateOf(Entry()));
    }

    [Fact]
    public void QuickMatchesFileNameAndShaPrefixes() {
        StagedEntry e = Entry();
        Assert.True(SessionFilter.Schema.Matches(e, ListQuery.Empty with { Quick = "egginc" }));
        Assert.True(SessionFilter.Schema.Matches(e, ListQuery.Empty with { Quick = "f00d" }));
        Assert.True(SessionFilter.Schema.Matches(e, ListQuery.Empty with { Quick = "abcdef" }));
        Assert.False(SessionFilter.Schema.Matches(e, ListQuery.Empty with { Quick = "0123456789" }));
    }

    [Fact]
    public void StateConditionSelectsOneBucket() {
        var pendingOnly = new ListQuery("", "", [
            new FilterGroup([new FilterCondition("state", FilterOp.Is, SessionFilter.Pending)])
        ]);
        Assert.True(SessionFilter.Schema.Matches(Entry("queued"), pendingOnly));
        Assert.False(SessionFilter.Schema.Matches(Entry(), pendingOnly));
    }

    [Fact]
    public void PlatformAndSizeFilter() {
        Assert.False(SessionFilter.Schema.Matches(Entry(platform: "ios"), ListQuery.Empty with { Platform = "android" }));
        var big = new ListQuery("", "", [new FilterGroup([new FilterCondition("sizeMb", FilterOp.AtLeast, "30")])]);
        Assert.True(SessionFilter.Schema.Matches(Entry(), big));
    }
}
