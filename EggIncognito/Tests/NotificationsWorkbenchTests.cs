using EggIdentity.Filters;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using EggIncognito.Services.Notifications;

namespace EggIncognito.Tests;

public class NotificationsWorkbenchTests {
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
    public void ResetNew_ClearsLabelSampleAndFilter() {
        var state = new NotificationsWorkbenchState();
        state.NewDraft.Label = "mine";
        state.NewDraft.SampleKey = "forward";
        state.NewDraft.ResetFilter(FeedFilter.Rows([FeedFilter.Is(ProtoBuildKind.Platform, "ios")]));
        state.ResetNew();
        Assert.Equal("", state.NewDraft.Label);
        Assert.Null(state.NewDraft.SampleKey);
        Assert.Equal(FilterCodec.Serialize(FeedEventKinds.Proto.Defaults), FilterCodec.Serialize(state.NewDraft.Filter));
    }

    [Fact]
    public void Draft_WithNoValidityReported_IsReady() {
        var draft = new NotificationDraft();
        Assert.True(draft.FilterReady);

        draft.Validity = new EggIdentity.UI.FilterDraftState(false, true, []);
        Assert.False(draft.FilterReady);

        draft.ResetFilter(FilterSet.Empty);
        Assert.True(draft.FilterReady);
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
