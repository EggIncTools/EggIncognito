using EggIdentity.Filters;
using EggIncognito.Controllers;
using EggIncognito.Data.Models;
using EggIncognito.Services.Contracts;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Events;
using EggIncognito.Services.Feed;
using EggIncognito.Services.Feed.Kinds;
using Ei;

namespace EggIncognito.Tests;

public class NotificationKindFilterTests {
    private static bool Sends(INotificationEvent evt, FilterSet set) =>
        FeedEventKinds.For(evt).Evaluate(evt, set).Matched;

    private static FeedSubscription Legacy(string kind, string trigger, string[]? platforms = null, params string[] filters) => new() {
        EventKind = kind,
        Trigger = trigger,
        Platforms = platforms ?? ["android", "ios"],
        Filters = filters
    };

    [Fact]
    public void EveryKind_HasADescription_AndValidDefaults() {
        foreach (var kind in FeedEventKinds.All) {
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
    public void EveryKind_HasAFixtureEvent() {
        foreach (var kind in FeedEventKinds.All) Assert.NotEmpty(FeedFixtures.Of(kind.Key));
    }

    [Fact]
    public void EveryEvent_AnswersEveryField_WithoutAShapeFailure() {
        foreach (var evt in FeedFixtures.All()) {
            var kind = FeedEventKinds.For(evt);
            foreach (var field in kind.Fields) {
                var probe = field.Kind switch {
                    FilterValueKind.Bool => FeedFilter.Rows([FeedFilter.Flag(field.Key, true)]),
                    FilterValueKind.Set => FeedFilter.Rows([FeedFilter.Lacks(field.Key, "probe-value-never-present")]),
                    FilterValueKind.Number => FeedFilter.Rows([FeedFilter.AtMost(field.Key, double.MaxValue)]),
                    FilterValueKind.Version => FeedFilter.Rows([new FilterCondition(field.Key, FilterOps.Gte, "0")]),
                    _ => FeedFilter.Rows([new FilterCondition(field.Key, FilterOps.NotEq, "probe-value-never-present")])
                };
                var result = kind.Evaluate(evt, probe);
                Assert.DoesNotContain(result.Failed, f => f.Reason.Contains("shape", StringComparison.Ordinal));
            }
        }
    }

    [Fact]
    public void EveryKind_HasAFixtureThatSendsOnDefaults() {
        foreach (var kind in FeedEventKinds.All)
            Assert.Contains(FeedFixtures.Of(kind.Key), e => kind.Evaluate(e, kind.Defaults).Matched);
    }

    [Fact]
    public void ProtoDefaults_SendForward_BlockBrokenRepeatAndBackfill() {
        var defaults = FeedEventKinds.Proto.Defaults;
        Assert.True(Sends(FeedFixtures.ProtoForward(), defaults));
        Assert.False(Sends(FeedFixtures.ProtoBroken(), defaults));
        Assert.False(Sends(FeedFixtures.ProtoBackfill(), defaults));
        Assert.False(Sends(FeedFixtures.ProtoRepeat(), defaults));
    }

    [Fact]
    public void ProtoDefaults_AreTwoConditions() => Assert.Equal(2, FeedEventKinds.Proto.Defaults.And.Count);

    [Fact]
    public void ProtoAppVersion_ComparesAsAVersion() {
        var set = FeedFilter.Rows([new FilterCondition(ProtoBuildKind.AppVersion, FilterOps.Gte, "1.10")]);
        Assert.True(Sends(FeedFixtures.ProtoForward(), set));
        Assert.False(Sends(FeedFixtures.ProtoBackfill(), set));
    }

    [Fact]
    public void LegacySuspect_ReachesTheBrokenBuild_AndNotACleanOne() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerSuspect, null,
            FeedEventKinds.FilterRequireProto, FeedEventKinds.FilterSaneBuild));
        Assert.True(Sends(FeedFixtures.ProtoBroken(), set));
        Assert.False(Sends(FeedFixtures.ProtoForward(), set));
    }

    [Fact]
    public void LegacyAllThreeGuards_CollapseToOneCleanCondition() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, null,
            FeedEventKinds.FilterRequireClientVersion, FeedEventKinds.FilterRequireProto, FeedEventKinds.FilterSaneBuild));
        Assert.Equal(2, set.And.Count);
        Assert.False(Sends(FeedFixtures.ProtoBroken(), set));
    }

    [Fact]
    public void LegacyPlatform_IsKept() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp, ["ios"]));
        Assert.False(Sends(FeedFixtures.ProtoForward(), set));
    }

    [Fact]
    public void LegacyGuards_OffMeansTheBrokenBuildSends() {
        var set = FeedEventKinds.Proto.Legacy(Legacy(FeedEventKinds.ProtoBuild, FeedEventKinds.TriggerVersionUp));
        Assert.True(Sends(FeedFixtures.ProtoBroken(), set));
    }

    [Fact]
    public void ConfigDefaults_BlockAnUnidentifiedChange() {
        var defaults = FeedEventKinds.Config.Defaults;
        Assert.False(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, null), defaults));
        Assert.True(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["events"], [], [])), defaults));
    }

    [Fact]
    public void ConfigLegacyFeedTrigger_MatchesOnlyThatResponse() {
        var set = FeedEventKinds.Config.Legacy(Legacy(FeedEventKinds.ConfigChanged, ConfigFeeds.Afx));
        var change = new ConfigChangeSummary(["artifacts"], [], []);
        Assert.True(Sends(FeedFixtures.Config(ConfigFeeds.Afx, change), set));
        Assert.False(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, change), set));
    }

    [Fact]
    public void ConfigChangedAspect_FiltersOnTheSetField() {
        var set = FeedFilter.Rows([FeedFilter.Has(ConfigChangedKind.Changed, "contracts")]);
        Assert.True(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["contracts"], [], [])), set));
        Assert.False(Sends(FeedFixtures.Config(ConfigFeeds.Config, new ConfigChangeSummary(["shells"], [], [])), set));
    }

    [Fact]
    public void NewColleggtible_IsAConfigCondition_FilteredByCountAndBuff() {
        var withEgg = FeedFixtures.Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["colleggtibles"], [], []),
            FeedFixtures.Colleggtible("a", GameModifier.Types.GameDimension.ShippingCapacity));
        var without = FeedFixtures.Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["colleggtibles"], [], []));

        var any = FeedFilter.Rows([FeedFilter.Above(ConfigChangedKind.NewColleggtibleCount, 0)]);
        Assert.True(Sends(withEgg, any));
        Assert.False(Sends(without, any));

        var shipping = FeedFilter.Rows([FeedFilter.Has(ConfigChangedKind.NewColleggtibleBuff, "ShippingCapacity")]);
        var laying = FeedFilter.Rows([FeedFilter.Has(ConfigChangedKind.NewColleggtibleBuff, "EggLayingRate")]);
        Assert.True(Sends(withEgg, shipping));
        Assert.False(Sends(withEgg, laying));
    }

    [Fact]
    public void LegacyColleggtibleRows_NormalizeToConfig_AndOnlySendNewColleggtibles() {
        Assert.Equal(FeedEventKinds.ConfigChanged, FeedEventKinds.Normalize(FeedEventKinds.LegacyColleggtible));
        var sub = new FeedSubscription { EventKind = FeedEventKinds.LegacyColleggtible, Trigger = "any" };
        var set = FeedEventKinds.Config.FilterOf(sub);
        Assert.False(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["events"], [], [])), set));
        Assert.True(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, null, FeedFixtures.Colleggtible("a")), set));
    }

    [Fact]
    public void LegacyColleggtibleRow_KeepsItsCountGuard_UnderAStoredSet() {
        var sub = new FeedSubscription {
            EventKind = FeedEventKinds.LegacyColleggtible,
            FilterSet = FilterCodec.Serialize(FilterSet.Empty)
        };
        var set = FeedEventKinds.Config.FilterOf(sub);
        Assert.False(Sends(FeedFixtures.Config(ConfigFeeds.Periodicals, new ConfigChangeSummary(["events"], [], [])), set));
    }

    [Fact]
    public void GameDataLegacyBinaryUp_OnlyMatchesAMovedBinary() {
        var set = FeedEventKinds.GameData.Legacy(Legacy(FeedEventKinds.GameDataRebuilt, FeedEventKinds.TriggerBinaryUp));
        Assert.True(Sends(FeedFixtures.GameData("2.0.0", "1.0.0", "eggs"), set));
        Assert.False(Sends(FeedFixtures.GameData("2.0.0", "2.0.0", "eggs"), set));
    }

    [Fact]
    public void EventDefaults_BlockTheHistoryImport() {
        var evt = FeedFixtures.GameEvent("old", false, GameEventSources.Carpet, TimeSpan.FromDays(400));
        var result = FeedEventKinds.Events.Evaluate(evt, FeedEventKinds.Events.Defaults);
        Assert.False(result.Matched);
        Assert.Equal(2, result.Failed.Count);
    }

    [Fact]
    public void ContractEggOr_MatchesEitherEgg() {
        var set = FeedFilter.Rows(FeedFilter.AnyOf(ContractReleaseKind.EggField, ["Fusion", "Tachyon"])!);
        Assert.True(Sends(FeedFixtures.ContractLive("a", Egg.Fusion), set));
        Assert.True(Sends(FeedFixtures.ContractLive("b", Egg.Tachyon), set));
        Assert.False(Sends(FeedFixtures.ContractLive("c", Egg.Dilithium), set));
    }

    [Fact]
    public void ContractLegacyTriggers_SplitByReleaseKind() {
        var newOnly = FeedEventKinds.Contracts.Legacy(Legacy(FeedEventKinds.ContractRelease, FeedEventKinds.TriggerNewOnly));
        Assert.True(Sends(FeedFixtures.ContractLive("a", Egg.Fusion), newOnly));
        Assert.False(Sends(FeedFixtures.ContractLive("b", Egg.Fusion, leggacy: true), newOnly));

        var ultra = FeedEventKinds.Contracts.Legacy(Legacy(FeedEventKinds.ContractRelease, FeedEventKinds.TriggerUltraOnly));
        Assert.True(Sends(FeedFixtures.ContractLive("c", Egg.Fusion, leggacy: true, ultra: true), ultra));
        Assert.False(Sends(FeedFixtures.ContractLive("d", Egg.Fusion, leggacy: true), ultra));
    }

    [Fact]
    public void ContractDeviceDefault_BlocksCarpetRows() {
        var carpet = FeedFixtures.Contract("a", Egg.Fusion, false, false, ContractSources.Carpet, TimeSpan.Zero);
        Assert.False(Sends(carpet, FeedEventKinds.Contracts.Defaults));
    }

    [Fact]
    public void UnreadableStoredFilter_FailsClosed() {
        var sub = new FeedSubscription { EventKind = FeedEventKinds.ProtoBuild, FilterSet = "not json" };
        Assert.False(Sends(FeedFixtures.ProtoForward(), FeedEventKinds.Proto.FilterOf(sub)));
    }

    [Fact]
    public void StoredUnknownField_FailsClosed_InsteadOfWidening() {
        var set = FeedFilter.Rows([new FilterCondition("removed_field", FilterOps.Eq, "x")]);
        Assert.False(Sends(FeedFixtures.ProtoForward(), set));
    }

    [Fact]
    public void Describe_NamesTheFieldOpAndValue() {
        var kind = FeedEventKinds.Proto;
        var result = kind.Evaluate(FeedFixtures.ProtoForward(), FeedFilter.Rows([FeedFilter.Is(ProtoBuildKind.Platform, "ios")]));
        string text = kind.Describe(result.Failed);
        Assert.Contains("Platform", text, StringComparison.Ordinal);
        Assert.Contains("ios", text, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewRows_CarryABodyForEverySample_EvenWhenNotMatched() {
        FeedSample[] samples = [
            new("f", "forward", FeedFixtures.ProtoForward()),
            new("b", "broken", FeedFixtures.ProtoBroken())
        ];
        var rows = ProtoFeedController.PreviewRows(FeedEventKinds.Proto, samples, FeedEventKinds.Proto.Defaults, null);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.Body)));
        Assert.Contains(rows, r => !r.Matches && r.BlockedBy.Count > 0);
        Assert.Contains(rows, r => r.Matches && r.BlockedBy.Count == 0);
    }

    [Fact]
    public void Vars_NoSamples_MeansEmptyExamples_NotInventedOnes() {
        foreach (var kind in FeedEventKinds.All)
            Assert.All(FeedVars.Describe(kind, []), v => Assert.Equal("", v.Example));
    }

    [Fact]
    public void Vars_ExamplesAreNotTruncated() {
        var evt = FeedFixtures.ProtoForward();
        var described = FeedVars.Describe(FeedEventKinds.Proto, [new FeedSample("a", "a", evt)]);
        Assert.Equal(evt.PageUrl, described.Single(v => v.Name == "pageUrl").Example);
    }
}
