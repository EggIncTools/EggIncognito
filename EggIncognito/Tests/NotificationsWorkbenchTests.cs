using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Notifications;

namespace EggIncognito.Tests;

public class NotificationsWorkbenchTests {
    private static FeedSubscription Probe(string kind, string trigger, params string[] filters) => new() {
        EventKind = kind,
        Trigger = trigger,
        Platforms = ["android", "ios"],
        Filters = filters
    };

    private static readonly string[] ProtoGuards = [
        FeedEventKinds.FilterRequireClientVersion, FeedEventKinds.FilterRequireProto,
        FeedEventKinds.FilterSaneBuild, FeedEventKinds.FilterKnownDelta
    ];

    private static (NotificationKind Kind, FeedSample Sample) Find(string kind, string key) {
        var info = FeedEventKinds.Find(kind);
        Assert.NotNull(info);
        var sample = FeedEventKinds.Sample(kind, key);
        Assert.NotNull(sample);
        return (info, sample);
    }

    [Fact]
    public void EveryKind_HasSamples_ADescription_AndAValidDefaultTrigger() {
        foreach (var kind in FeedEventKinds.All) {
            Assert.NotEmpty(kind.Samples);
            Assert.False(string.IsNullOrWhiteSpace(kind.Description));
            Assert.NotNull(kind.Trigger(kind.DefaultTrigger));
            Assert.All(kind.BypassFilters, t => Assert.NotNull(kind.Trigger(t)));
        }
    }

    [Fact]
    public void UnknownKind_HasNoSamples() => Assert.Empty(FeedEventKinds.Samples("not_a_kind"));

    [Fact]
    public void EveryKind_HasASampleThatWouldSendOnDefaults() {
        foreach (var kind in FeedEventKinds.All) {
            var probe = Probe(kind.Key, kind.DefaultTrigger, FeedEventKinds.NormalizeFilters(kind.Key, null));
            Assert.Contains(kind.Samples, s => kind.Matches(s.Event, probe) && kind.BlockedBy(s.Event, probe).Count == 0);
        }
    }

    [Fact]
    public void BrokenSample_BlockedByDefaultGuards() {
        var (kind, broken) = Find(FeedEventKinds.ProtoBuild, "broken");
        var sub = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerNewVersion, ProtoGuards);
        Assert.True(kind.Matches(broken.Event, sub));
        Assert.NotEmpty(kind.BlockedBy(broken.Event, sub));
    }

    [Fact]
    public void BrokenSample_ReachesSuspect() {
        var (kind, broken) = Find(FeedEventKinds.ProtoBuild, "broken");
        var sub = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerSuspect, ProtoGuards);
        Assert.True(kind.Matches(broken.Event, sub));
        Assert.Empty(kind.BlockedBy(broken.Event, sub));
    }

    [Fact]
    public void ForwardSample_PassesGuardsOnVersionUp() {
        var (kind, forward) = Find(FeedEventKinds.ProtoBuild, "forward");
        var sub = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ProtoGuards);
        Assert.True(kind.Matches(forward.Event, sub));
        Assert.Empty(kind.BlockedBy(forward.Event, sub));
    }

    [Fact]
    public void BackfillSample_DoesNotMatchVersionUp() {
        var (kind, backfill) = Find(FeedEventKinds.ProtoBuild, "backfill");
        Assert.False(kind.Matches(backfill.Event,
            Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ProtoGuards)));
    }

    [Fact]
    public void BareConfigSample_BlockedWhenAspectsRequired() {
        var (kind, bare) = Find(FeedEventKinds.ConfigChanged, "bare");
        var sub = Probe(FeedEventKinds.ConfigChanged, FeedEventKinds.TriggerAnyFeed, FeedEventKinds.FilterRequireAspects);
        Assert.True(kind.Matches(bare.Event, sub));
        Assert.Equal(FeedEventKinds.FilterRequireAspects, Assert.Single(kind.BlockedBy(bare.Event, sub)));

        var (_, identified) = Find(FeedEventKinds.ConfigChanged, "periodicals");
        Assert.Empty(kind.BlockedBy(identified.Event, sub));
    }

    [Fact]
    public void AfxSample_HasAspects_ButNoIdentifiers() {
        var (kind, afx) = Find(FeedEventKinds.ConfigChanged, "afx");
        var aspects = Probe(FeedEventKinds.ConfigChanged, FeedEventKinds.TriggerAnyFeed, FeedEventKinds.FilterRequireAspects);
        Assert.Empty(kind.BlockedBy(afx.Event, aspects));

        var ids = Probe(FeedEventKinds.ConfigChanged, FeedEventKinds.TriggerAnyFeed, FeedEventKinds.FilterRequireIds);
        Assert.Equal(FeedEventKinds.FilterRequireIds, Assert.Single(kind.BlockedBy(afx.Event, ids)));
    }

    [Fact]
    public void GameDataSamples_OnlyBinaryUpMatchesTheBinaryUpTrigger() {
        var (kind, moved) = Find(FeedEventKinds.GameDataRebuilt, "binary_up");
        var (_, same) = Find(FeedEventKinds.GameDataRebuilt, "same_binary");

        var sub = Probe(FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerBinaryUp);
        Assert.True(kind.Matches(moved.Event, sub));
        Assert.False(kind.Matches(same.Event, sub));

        var any = Probe(FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerAnyRebuild);
        Assert.True(kind.Matches(moved.Event, any));
        Assert.True(kind.Matches(same.Event, any));
    }

    [Fact]
    public void EventBackfillSample_BlockedByBothDefaultFilters() {
        var (kind, old) = Find(FeedEventKinds.GameEvent, "backfill");
        var sub = Probe(FeedEventKinds.GameEvent, FeedEventKinds.TriggerAny,
            FeedEventKinds.FilterFreshOnly, FeedEventKinds.FilterDeviceOnly);
        Assert.True(kind.Matches(old.Event, sub));
        Assert.Equal([FeedEventKinds.FilterFreshOnly, FeedEventKinds.FilterDeviceOnly], kind.BlockedBy(old.Event, sub));
    }

    [Fact]
    public void ContractSamples_SplitAcrossTriggers() {
        var (kind, fresh) = Find(FeedEventKinds.ContractRelease, "new");
        var (_, leggacy) = Find(FeedEventKinds.ContractRelease, "leggacy");
        var (_, ultra) = Find(FeedEventKinds.ContractRelease, "ultra");

        Assert.True(kind.Matches(fresh.Event, Probe(kind.Key, FeedEventKinds.TriggerNewOnly)));
        Assert.False(kind.Matches(leggacy.Event, Probe(kind.Key, FeedEventKinds.TriggerNewOnly)));
        Assert.True(kind.Matches(leggacy.Event, Probe(kind.Key, FeedEventKinds.TriggerLeggacyOnly)));
        Assert.False(kind.Matches(leggacy.Event, Probe(kind.Key, FeedEventKinds.TriggerUltraOnly)));
        Assert.True(kind.Matches(ultra.Event, Probe(kind.Key, FeedEventKinds.TriggerUltraOnly)));
    }

    [Fact]
    public void PreviewRows_CarryABodyForEverySample_EvenWhenNotMatched() {
        var probe = Probe(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ProtoGuards);
        var rows = ProtoFeedController.PreviewRows(probe);

        Assert.Equal(FeedEventKinds.Proto.Samples.Count, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Body)));
        Assert.Contains(rows, r => !r.Matches);
        Assert.Contains(rows, r => r.Matches && r.BlockedBy.Count > 0);
        Assert.Contains(rows, r => r.Matches && r.BlockedBy.Count == 0);
    }

    [Fact]
    public void SampleBodies_AreNonEmptyJson() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in kind.Samples) {
                string body = DiscordFeedPayload.Build(sample.Event);
                Assert.StartsWith("{", body, StringComparison.Ordinal);
                Assert.Contains("embeds", body, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("#notify", true, null)]
    [InlineData("#notify_7", true, 7)]
    [InlineData("#notify_abc", true, null)]
    [InlineData("#android_111358", false, null)]
    [InlineData("#data/periodical/get_periodicals", false, null)]
    [InlineData("", false, null)]
    public void ParseHash_Grammar(string hash, bool match, int? id) {
        (bool gotMatch, int? gotId) = NotificationsWorkbenchState.ParseHash(hash);

        Assert.Equal(match, gotMatch);
        Assert.Equal(id, gotId);
    }

    [Theory]
    [InlineData("#notify_7_preview")]
    [InlineData("#notify_7_history")]
    [InlineData("#notify_7_bogus")]
    [InlineData("#notify_7_history_extra")]
    public void ParseHash_IgnoresTheLegacyModeSegment(string hash) {
        (bool match, int? id) = NotificationsWorkbenchState.ParseHash(hash);

        Assert.True(match);
        Assert.Equal(7, id);
    }

    [Fact]
    public void Hash_RoundTrips() {
        var state = new NotificationsWorkbenchState();
        Assert.Equal("notify", state.Hash());

        state.Creating = false;
        state.SelectedId = 12;
        Assert.Equal("notify_12", state.Hash());

        (bool match, int? id) = NotificationsWorkbenchState.ParseHash(state.Hash());
        Assert.True(match);
        Assert.Equal(12, id);
    }

    [Fact]
    public void ApplyHash_RestoresTheSelectionFromALegacyLink() {
        var state = new NotificationsWorkbenchState();

        Assert.True(state.ApplyHash("#notify_7_history"));
        Assert.False(state.Creating);
        Assert.Equal(7, state.SelectedId);

        Assert.True(state.ApplyHash("#notify"));
        Assert.True(state.Creating);
        Assert.Null(state.SelectedId);

        Assert.False(state.ApplyHash("#android_111358"));
    }

    [Fact]
    public void ResetNew_ClearsLabelAndSample() {
        var state = new NotificationsWorkbenchState();
        state.NewDraft.Label = "mine";
        state.NewDraft.SampleKey = "forward";
        state.ResetNew();
        Assert.Equal("", state.NewDraft.Label);
        Assert.Null(state.NewDraft.SampleKey);
    }

    [Fact]
    public void TheWorkbenchHasNoModes() {
        var state = new NotificationsWorkbenchState();

        Assert.Empty(state.Modes);
        Assert.Equal("", state.DefaultMode);
        Assert.Equal("", state.Mode);
        Assert.True(state.OwnsHash("#notify_7"));
        Assert.False(state.OwnsHash("#data/periodical/get_periodicals"));
    }
}
