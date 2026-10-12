using EggIdentity.Filters;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;

namespace EggIncognito.Tests;

public class NotificationKindFilterTests {
    private static (NotificationKind Kind, INotificationEvent Event) Find(string kind, string key) {
        var info = FeedEventKinds.Find(kind);
        Assert.NotNull(info);
        var sample = FeedEventKinds.Sample(kind, key);
        Assert.NotNull(sample);
        return (info, sample.Event);
    }

    private static bool Sends(string kind, string sample, FilterSet set) {
        var (info, evt) = Find(kind, sample);
        return info.Evaluate(evt, set).Matched;
    }

    private static FeedSubscription Legacy(string kind, string trigger, string[]? platforms = null, params string[] filters) => new() {
        EventKind = kind,
        Trigger = trigger,
        Platforms = platforms ?? ["android", "ios"],
        Filters = filters
    };

    [Fact]
    public void EveryKind_HasSamples_ADescription_AndValidDefaults() {
        foreach (var kind in FeedEventKinds.All) {
            Assert.NotEmpty(kind.Samples);
            Assert.False(string.IsNullOrWhiteSpace(kind.Description));
            Assert.Empty(kind.Validate(kind.Defaults));
        }
    }

    [Fact]
    public void EveryKind_FieldKeys_AreUnique() {
        foreach (var kind in FeedEventKinds.All)
            Assert.Equal(kind.Fields.Count, kind.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryKind_EverySampleAnswersEveryField_WithoutAShapeFailure() {
        foreach (var kind in FeedEventKinds.All) {
            foreach (var sample in kind.Samples) {
                foreach (var field in kind.Fields) {
                    var probe = FeedFilter.Rows([new FilterCondition(field.Key, FilterOps.NotEq, "probe-value-never-present")]);
                    if (field.Kind == FilterValueKind.Bool) probe = FeedFilter.Rows([FeedFilter.Flag(field.Key, true)]);
                    if (field.Kind == FilterValueKind.Set) probe = FeedFilter.Rows([FeedFilter.Lacks(field.Key, "probe-value-never-present")]);
                    if (field.Kind is FilterValueKind.Number) probe = FeedFilter.Rows([FeedFilter.AtMost(field.Key, double.MaxValue)]);
                    if (field.Kind is FilterValueKind.Version) probe = FeedFilter.Rows([new FilterCondition(field.Key, FilterOps.Gte, "0")]);
                    var result = kind.Evaluate(sample.Event, probe);
                    Assert.DoesNotContain(result.Failed, f => f.Reason.Contains("shape", StringComparison.Ordinal));
                }
            }
        }
    }

    [Fact]
    public void EveryKind_HasASampleThatSendsOnDefaults() {
        foreach (var kind in FeedEventKinds.All)
            Assert.Contains(kind.Samples, s => kind.Evaluate(s.Event, kind.Defaults).Matched);
    }

    [Fact]
    public void UnknownKind_HasNoSamples() => Assert.Empty(FeedEventKinds.Samples("not_a_kind"));

    [Fact]
    public void ProtoDefaults_SendForward_BlockBrokenAndBackfill() {
        var defaults = FeedEventKinds.Proto.Defaults;
        Assert.True(Sends(FeedEventKinds.ProtoBuild, "forward", defaults));
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "broken", defaults));
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "backfill", defaults));
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "proto_only", defaults));
    }

    [Fact]
    public void ProtoAppVersion_ComparesAsAVersion() {
        var set = FeedFilter.Rows([new FilterCondition(ProtoBuildKind.AppVersion, FilterOps.Gte, "1.37")]);
        Assert.True(Sends(FeedEventKinds.ProtoBuild, "forward", set));
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "backfill", set));
    }

    [Fact]
    public void LegacySuspect_ReachesTheBrokenBuild_AndNotACleanOne() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerSuspect, null,
            FeedEventKinds.FilterRequireProto, FeedEventKinds.FilterSaneBuild));
        Assert.True(Sends(FeedEventKinds.ProtoBuild, "broken", set));
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "forward", set));
    }

    [Fact]
    public void LegacyPlatform_IsKept() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ["ios"]));
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "forward", set));
    }

    [Fact]
    public void LegacyGuards_OffMeansTheBrokenBuildSends() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp));
        Assert.True(Sends(FeedEventKinds.ProtoBuild, "broken", set));
    }

    [Fact]
    public void ConfigDefaults_BlockAnUnidentifiedChange() {
        var defaults = FeedEventKinds.Config.Defaults;
        Assert.False(Sends(FeedEventKinds.ConfigChanged, "bare", defaults));
        Assert.True(Sends(FeedEventKinds.ConfigChanged, "periodicals", defaults));
    }

    [Fact]
    public void ConfigLegacyFeedTrigger_MatchesOnlyThatResponse() {
        var set = FeedEventKinds.Config.Legacy(Legacy(FeedEventKinds.ConfigChanged, "afx-config"));
        Assert.True(Sends(FeedEventKinds.ConfigChanged, "afx", set));
        Assert.False(Sends(FeedEventKinds.ConfigChanged, "periodicals", set));
    }

    [Fact]
    public void ConfigChangedAspect_FiltersOnTheSetField() {
        var set = FeedFilter.Rows([FeedFilter.Has(ConfigChangedKind.Changed, "contracts")]);
        Assert.True(Sends(FeedEventKinds.ConfigChanged, "periodicals", set));
        Assert.False(Sends(FeedEventKinds.ConfigChanged, "dlc", set));
    }

    [Fact]
    public void GameDataLegacyBinaryUp_OnlyMatchesAMovedBinary() {
        var set = FeedEventKinds.GameData.Legacy(Legacy(FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerBinaryUp));
        Assert.True(Sends(FeedEventKinds.GameDataRebuilt, "binary_up", set));
        Assert.False(Sends(FeedEventKinds.GameDataRebuilt, "same_binary", set));
    }

    [Fact]
    public void EventDefaults_BlockTheHistoryImport() {
        var (kind, evt) = Find(FeedEventKinds.GameEvent, "backfill");
        var result = kind.Evaluate(evt, kind.Defaults);
        Assert.False(result.Matched);
        Assert.Equal(2, result.Failed.Count);
    }

    [Fact]
    public void ContractEggOr_MatchesEitherEgg() {
        var set = FeedFilter.Rows(FeedFilter.AnyOf(ContractReleaseKind.EggField, ["Fusion", "Tachyon"])!);
        Assert.True(Sends(FeedEventKinds.ContractRelease, "new", set));
        Assert.True(Sends(FeedEventKinds.ContractRelease, "leggacy", set));
        Assert.False(Sends(FeedEventKinds.ContractRelease, "ultra", set));
    }

    [Fact]
    public void ContractLegacyTriggers_SplitByReleaseKind() {
        var newOnly = FeedEventKinds.Contracts.Legacy(Legacy(FeedEventKinds.ContractRelease, FeedEventKinds.TriggerNewOnly));
        Assert.True(Sends(FeedEventKinds.ContractRelease, "new", newOnly));
        Assert.False(Sends(FeedEventKinds.ContractRelease, "leggacy", newOnly));

        var ultra = FeedEventKinds.Contracts.Legacy(Legacy(FeedEventKinds.ContractRelease, FeedEventKinds.TriggerUltraOnly));
        Assert.True(Sends(FeedEventKinds.ContractRelease, "ultra", ultra));
        Assert.False(Sends(FeedEventKinds.ContractRelease, "leggacy", ultra));
    }

    [Fact]
    public void ColleggtibleBuffDimension_FiltersOnTheSetField() {
        var hit = FeedFilter.Rows([FeedFilter.Has(ColleggtibleKind.BuffDimension, "ShippingCapacity")]);
        var miss = FeedFilter.Rows([FeedFilter.Has(ColleggtibleKind.BuffDimension, "EggLayingRate")]);
        Assert.True(Sends(FeedEventKinds.Colleggtible, "new", hit));
        Assert.False(Sends(FeedEventKinds.Colleggtible, "new", miss));
    }

    [Fact]
    public void UnreadableStoredFilter_FailsClosed() {
        var sub = new FeedSubscription { EventKind = FeedEventKinds.ProtoBuild, FilterSet = "not json" };
        var (kind, evt) = Find(FeedEventKinds.ProtoBuild, "forward");
        Assert.False(kind.Evaluate(evt, kind.FilterOf(sub)).Matched);
    }

    [Fact]
    public void StoredUnknownField_FailsClosed_InsteadOfWidening() {
        var set = FeedFilter.Rows([new FilterCondition("removed_field", FilterOps.Eq, "x")]);
        Assert.False(Sends(FeedEventKinds.ProtoBuild, "forward", set));
    }

    [Fact]
    public void Describe_NamesTheFieldOpAndValue() {
        var (kind, evt) = Find(FeedEventKinds.ProtoBuild, "forward");
        var result = kind.Evaluate(evt, FeedFilter.Rows([FeedFilter.Is(ProtoBuildKind.Platform, "ios")]));
        string text = kind.Describe(result.Failed);
        Assert.Contains("Platform", text, StringComparison.Ordinal);
        Assert.Contains("ios", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewRows_CarryABodyForEverySample_EvenWhenNotMatched() {
        var rows = ProtoFeedController.PreviewRows(FeedEventKinds.Proto, FeedEventKinds.Proto.Defaults, null);

        Assert.Equal(FeedEventKinds.Proto.Samples.Count, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Body)));
        Assert.Contains(rows, r => !r.Matches && r.BlockedBy.Count > 0);
        Assert.Contains(rows, r => r.Matches && r.BlockedBy.Count == 0);
    }
}
