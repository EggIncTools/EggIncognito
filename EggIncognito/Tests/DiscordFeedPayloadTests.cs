using System.Text.Json;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;

namespace EggIncognito.Tests;

public class DiscordFeedPayloadTests {
    private static ProtoBuildEvent Proto(string platform, string appVersion, string build, string? client, string sha,
        bool changed, string url) =>
        new(1, platform, appVersion, build, client, sha, true, changed, url, VersionDelta.Forward);

    [Fact]
    public void Build_Changed_ContainsVersionLabelBuildAndShortSha() {
        string json = DiscordFeedPayload.Build(Proto(
            "android", "1.99.0", "111343", "72", "abcdef0123456789deadbeef", true,
            "https://eggincognito.egginc.tools/protos/android/111343"));

        Assert.Contains("Egg, Inc. 1.99.0 (build 111343, android)", json);
        Assert.Contains("changed", json);
        Assert.Contains("72", json);
        Assert.Contains("abcdef012345", json);
        Assert.DoesNotContain("deadbeef", json);
    }

    [Fact]
    public void Build_Unchanged_LabelsUnchanged() {
        string json = DiscordFeedPayload.Build(Proto("ios", "1.99.0", "111343", null, "shortsha", false, "https://x/y"));
        Assert.Contains("unchanged", json);
        Assert.Contains("shortsha", json);
        Assert.DoesNotContain("Client", json);
    }

    [Fact]
    public void BuildPageUrl_DefaultsToMainHost_NotAbandonedSubdomain() {
        string url = FeedDispatcher.BuildPageUrl(null, "android", "111343");
        Assert.Equal("https://eggincognito.egginc.tools/protos/android/111343", url);
        Assert.DoesNotContain("protos.eggincognito", url);
    }

    [Fact]
    public void BuildPageUrl_HonorsConfiguredBaseUrl_TrimmingSlash() {
        string url = FeedDispatcher.BuildPageUrl("https://example.test/", "ios", "1.36.0.2");
        Assert.Equal("https://example.test/protos/ios/1.36.0.2", url);
    }

    [Fact]
    public void MarkAsTest_Embed_AddsVisibleContentNoticeAndFooter() {
        string real = DiscordFeedPayload.Build(Proto(
            "android", "1.37.0", "111358", "72", "abcdef0123456789", true,
            "https://eggincognito.egginc.tools/protos/android/111358"));
        Assert.DoesNotContain(DiscordFeedPayload.TestNotice, real, StringComparison.Ordinal);

        string marked = DiscordFeedPayload.MarkAsTest(real);

        using var doc = JsonDocument.Parse(marked);
        Assert.Equal(DiscordFeedPayload.TestNotice, doc.RootElement.GetProperty("content").GetString());
        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.Equal(DiscordFeedPayload.TestNotice, embed.GetProperty("footer").GetProperty("text").GetString());
        Assert.Equal("Egg, Inc. 1.37.0 (build 111358, android)", embed.GetProperty("title").GetString());
    }

    [Fact]
    public void MarkAsTest_CustomTemplate_KeepsBodyBelowTheNotice() {
        string real = DiscordFeedPayload.Build(
            Proto("ios", "1.37.0", "1.37.0.1", null, "abcdef0123456789", true, "https://x/y"),
            "New build {{appVersion}} is up");

        string marked = DiscordFeedPayload.MarkAsTest(real);

        using var doc = JsonDocument.Parse(marked);
        Assert.Equal($"{DiscordFeedPayload.TestNotice}\nNew build 1.37.0 is up",
            doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void MarkAsTest_EverySample_CarriesTheNotice() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in kind.Samples) {
                string marked = DiscordFeedPayload.MarkAsTest(DiscordFeedPayload.Build(sample.Event));
                Assert.Contains(DiscordFeedPayload.TestNotice, marked, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Config_NamesTheResponse_AndListsAspects() {
        string json = DiscordFeedPayload.Build(new ConfigChangedEvent(
            "config", "abcdef0123456789deadbeef", "https://x/data",
            new ConfigChangeSummary(["shellSets"], ["shellSet:glacier"], [])));

        Assert.Contains("Egg, Inc. Game config changed", json);
        Assert.Contains("shellSet:glacier", json);
        Assert.Contains("abcdef012345", json);
        Assert.DoesNotContain("deadbeef", json);
        Assert.DoesNotContain("Removed", json);
    }

    [Fact]
    public void Config_Template_RendersEveryVariable() {
        string json = DiscordFeedPayload.Build(new ConfigChangedEvent(
                "afx-config", "sha1", "https://x/data",
                new ConfigChangeSummary(["artifacts"], ["artifact:ORNATE_GUSSET"], ["artifact:LUNAR_TOTEM"])),
            "{{feed}}|{{feedLabel}}|{{sha}}|{{pageUrl}}|{{changed}}|{{added}}|{{removed}}");

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(
            "afx-config|Artifacts config|sha1|https://x/data|artifacts|artifact:ORNATE_GUSSET|artifact:LUNAR_TOTEM",
            doc.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public void GameData_ShowsBinaryAndChangedDocuments() {
        string json = DiscordFeedPayload.Build(new GameDataRebuiltEvent(
            "1.37.0", "1.36.4", "android", "sha", ["eggs", "research"], "https://x/data"));

        Assert.Contains("Egg, Inc. game data rebuilt from 1.37.0", json);
        Assert.Contains("eggs, research", json);
        Assert.Contains("1.36.4", json);
    }

    [Fact]
    public void EverySample_HasNonEmptyEmbedFields() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in kind.Samples) {
                using var doc = JsonDocument.Parse(DiscordFeedPayload.Build(sample.Event));
                var fields = doc.RootElement.GetProperty("embeds")[0].GetProperty("fields");
                foreach (var field in fields.EnumerateArray())
                    Assert.False(string.IsNullOrEmpty(field.GetProperty("value").GetString()), $"{kind.Key}/{sample.Key}");
            }
        }
    }

    [Fact]
    public void EveryKind_DeclaredVars_MatchTheEventVars() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in kind.Samples) {
                Assert.Equal(kind.Vars.OrderBy(v => v, StringComparer.Ordinal),
                    sample.Event.Vars().Keys.OrderBy(v => v, StringComparer.Ordinal));
            }
        }
    }

    [Fact]
    public void MarkAsTest_NonObjectBody_ReturnedUnchanged() {
        Assert.Equal("not json", DiscordFeedPayload.MarkAsTest("not json"));
        Assert.Equal("[1,2]", DiscordFeedPayload.MarkAsTest("[1,2]"));
    }
}
