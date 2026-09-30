using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using EggIncognito.Services.Feed;

namespace EggIncognito.Tests;

public class FeedTriggerTests {
    private static readonly string[] AllTriggers = [
        FeedEventKinds.TriggerVersionUp, FeedEventKinds.TriggerProtoChanged,
        FeedEventKinds.TriggerNewVersion, FeedEventKinds.TriggerSuspect
    ];

    [Fact]
    public void ProtoChanged_FiresOnlyWhenChanged() {
        Assert.True(FeedTrigger.Matches(FeedEventKinds.TriggerProtoChanged, true, true, VersionDelta.Forward, false,
            ["android"], "android"));
        Assert.False(FeedTrigger.Matches(FeedEventKinds.TriggerProtoChanged, true, false, VersionDelta.Forward, false,
            ["android"], "android"));
    }

    [Fact]
    public void NewVersion_FiresOnForwardInsertOnly() {
        Assert.True(FeedTrigger.Matches(FeedEventKinds.TriggerNewVersion, true, false, VersionDelta.Forward, false,
            ["android"], "android"));
        Assert.False(FeedTrigger.Matches(FeedEventKinds.TriggerNewVersion, false, false, VersionDelta.Forward, false,
            ["android"], "android"));
    }

    [Fact]
    public void VersionUp_FiresOnlyOnForwardDelta() {
        Assert.True(FeedTrigger.Matches(FeedEventKinds.TriggerVersionUp, true, false, VersionDelta.Forward, false,
            ["android"], "android"));
        Assert.False(FeedTrigger.Matches(FeedEventKinds.TriggerVersionUp, true, false, VersionDelta.Backfill, false,
            ["android"], "android"));
    }

    [Fact]
    public void Suspect_FiresOnFlawedForwardOnly() {
        Assert.True(FeedTrigger.Matches(FeedEventKinds.TriggerSuspect, true, false, VersionDelta.Forward, true,
            ["android"], "android"));
        Assert.False(FeedTrigger.Matches(FeedEventKinds.TriggerSuspect, true, false, VersionDelta.Forward, false,
            ["android"], "android"));
    }

    [Theory]
    [InlineData(VersionDelta.Backfill)]
    [InlineData(VersionDelta.Repeat)]
    [InlineData(VersionDelta.Unknown)]
    public void NothingFiresUnlessTheVersionIsNewerThanTheNewestStored(VersionDelta delta) {
        foreach (string trigger in AllTriggers) {
            Assert.False(FeedTrigger.Matches(trigger, true, true, delta, true, ["android", "ios"], "android"),
                $"{trigger} fired on {delta}");
        }
    }

    [Fact]
    public void SubscriptionDefaults_MatchDeclaredEventKind() {
        var sub = new FeedSubscription();
        Assert.Equal(FeedEventKinds.ProtoBuild, sub.EventKind);
        Assert.Equal(FeedEventKinds.Proto.DefaultTrigger, sub.Trigger);
    }

    [Fact]
    public void PlatformFilter_Excludes() =>
        Assert.False(FeedTrigger.Matches(FeedEventKinds.TriggerNewVersion, true, true, VersionDelta.Forward, false,
            ["ios"], "android"));
}
