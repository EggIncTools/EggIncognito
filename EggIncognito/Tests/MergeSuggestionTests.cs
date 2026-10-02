using EggIncognito.Data.Services;

namespace EggIncognito.Tests;

public class MergeSuggestionTests {
    private static ProtoRegistryStore.MergeCandidate Row(
        int id, int? canonical, string platform, string app, string build, string? client, string sha) =>
        new(id, canonical, platform, app, build, client, sha, DateTimeOffset.UnixEpoch);

    [Fact]
    public void SameShaAndClient_SeparatedByAnotherSha_IsSuggestedAcrossAppVersions() {
        var rows = new[] {
            Row(1, null, "ios", "1.35.8", "1.35.8.0", "72", "9a9ffa"),
            Row(2, 1, "android", "1.35.7", "111344", "72", "9a9ffa"),
            Row(3, 1, "android", "1.35.7", "111343", "72", "9a9ffa"),
            Row(4, 1, "ios", "1.35.7", "1.35.7.0", "72", "9a9ffa"),
            Row(5, null, "android", "1.35.7", "111342", "72", "d71fb8"),
            Row(6, null, "ios", "1.35.6", "1.35.6.3", "72", "9a9ffa"),
        };

        var s = Assert.Single(ProtoRegistryStore.SuggestMerges(rows));

        Assert.Equal("9a9ffa", s.ProtoSha);
        Assert.Equal("72", s.ClientVersion);
        Assert.Equal(2, s.Members.Count);
        Assert.Contains(s.Members, m => m.Build == "1.35.8.0" && m.AppVersion == "1.35.8");
        Assert.Contains(s.Members, m => m.Build == "1.35.6.3" && m.AppVersion == "1.35.6");
        Assert.DoesNotContain(s.Members, m => m.Build == "111344");
    }

    [Fact]
    public void SameShaDifferentClient_IsNotSuggested() {
        var rows = new[] {
            Row(1, null, "android", "1.37", "111353", "75", "4a17bc"),
            Row(2, null, "android", "1.37", "111354", "73", "4a17bc"),
        };

        Assert.Empty(ProtoRegistryStore.SuggestMerges(rows));
    }

    [Fact]
    public void SamePlatformSameShaAndClient_IsSuggested() {
        var rows = new[] {
            Row(1, null, "android", "1.37", "111356", "75", "4a17bc"),
            Row(2, null, "android", "1.37", "111355", "75", "4a17bc"),
        };

        var s = Assert.Single(ProtoRegistryStore.SuggestMerges(rows));
        Assert.Equal(2, s.Members.Count);
    }

    [Fact]
    public void RowsAlreadyInOneRelease_AreNotSuggested() {
        var rows = new[] {
            Row(1, null, "android", "1.37", "111356", "75", "4a17bc"),
            Row(2, 1, "android", "1.37", "111355", "75", "4a17bc"),
            Row(3, 1, "ios", "1.37.0.1", "1.37.0.1", "75", "4a17bc"),
        };

        Assert.Empty(ProtoRegistryStore.SuggestMerges(rows));
    }

    [Fact]
    public void MissingClientVersion_IsNeverSuggested() {
        var rows = new[] {
            Row(1, null, "android", "1.36", "111350", null, "4f4397"),
            Row(2, null, "ios", "1.36", "1.36.0.2", "", "4f4397"),
        };

        Assert.Empty(ProtoRegistryStore.SuggestMerges(rows));
    }

    [Fact]
    public void ShaComparison_IgnoresCaseAndWhitespace() {
        var rows = new[] {
            Row(1, null, "android", "1.36", "111350", "72", "4F4397 "),
            Row(2, null, "ios", "1.36", "1.36.0.2", " 72", "4f4397"),
        };

        Assert.Single(ProtoRegistryStore.SuggestMerges(rows));
    }
}
