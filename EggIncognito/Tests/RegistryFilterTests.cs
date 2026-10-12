using System.Globalization;
using EggIncognito.Services.Filtering;
using EggIncognito.Services.Protos;

namespace EggIncognito.Tests;

public class RegistryFilterTests {
    private static ProtoRegistryRow Row(
        string platform = "ios",
        string? app = "1.36.0",
        string build = "1.36.0.2",
        string? client = "74",
        string? source = "device",
        string? package = "com.auxbrain.egginc",
        string? sha = "abcdef0123456789",
        DateTime? detected = null,
        string? flag = null,
        int? order = null,
        bool? archived = null) =>
        new(1, null, platform, app, build, client, source, package, sha, detected, flag, order, archived);

    [Fact]
    public void ArchivedFieldIsVisibleToEveryoneAndTreatsUnknownAsFalse() {
        Assert.True(RegistryFilter.Matches(Row(archived: true), One("archived", EgiFilterOp.True, "")));
        Assert.False(RegistryFilter.Matches(Row(archived: false), One("archived", EgiFilterOp.True, "")));
        Assert.True(RegistryFilter.Matches(Row(archived: null), One("archived", EgiFilterOp.False, "")));
        Assert.Contains(RegistryFilter.FieldsFor(true), f => f.Key == "archived");
        Assert.Contains(RegistryFilter.FieldsFor(false), f => f.Key == "archived");
    }

    private static ListQuery One(string field, EgiFilterOp op, string value) =>
        new("", "", [new EgiFilterGroup([new EgiFilterCondition(field, op, value)])]);

    [Fact]
    public void EmptyQueryPassesEverything() {
        Assert.True(RegistryFilter.Matches(Row(), ListQuery.Empty));
        Assert.True(ListQuery.Empty.IsEmpty);
    }

    [Fact]
    public void QuickMatchesAppVersionBuildAndClientVersion() {
        ProtoRegistryRow row = Row();
        Assert.True(RegistryFilter.Matches(row, ListQuery.Empty with { Quick = "1.36" }));
        Assert.True(RegistryFilter.Matches(row, ListQuery.Empty with { Quick = "0.2" }));
        Assert.True(RegistryFilter.Matches(row, ListQuery.Empty with { Quick = "74" }));
        Assert.False(RegistryFilter.Matches(row, ListQuery.Empty with { Quick = "nothing" }));
    }

    [Fact]
    public void QuickMatchesShaByPrefixOnly() {
        ProtoRegistryRow row = Row(sha: "abcdef0123456789");
        Assert.True(RegistryFilter.Matches(row, ListQuery.Empty with { Quick = "abcdef" }));
        Assert.False(RegistryFilter.Matches(row, ListQuery.Empty with { Quick = "0123456789" }));
    }

    [Fact]
    public void PlatformIsCaseInsensitiveAndExact() {
        Assert.True(RegistryFilter.Matches(Row(platform: "ios"), ListQuery.Empty with { Platform = "IOS" }));
        Assert.False(RegistryFilter.Matches(Row(platform: "ios"), ListQuery.Empty with { Platform = "android" }));
    }

    [Fact]
    public void EqualityOperatorsRunOverSelectFields() {
        ProtoRegistryRow row = Row(source: "device");
        Assert.True(RegistryFilter.Matches(row, One("source", EgiFilterOp.Is, "DEVICE")));
        Assert.False(RegistryFilter.Matches(row, One("source", EgiFilterOp.IsNot, "device")));
        Assert.True(RegistryFilter.Matches(row, One("source", EgiFilterOp.IsNot, "crawl")));
    }

    [Fact]
    public void TextOperatorsRunOverBuildAndSha() {
        ProtoRegistryRow row = Row(build: "1.36.0.2", sha: "abcdef0123456789");
        Assert.True(RegistryFilter.Matches(row, One("build", EgiFilterOp.Is, "1.36.0.2")));
        Assert.True(RegistryFilter.Matches(row, One("build", EgiFilterOp.Contains, "36.0")));
        Assert.False(RegistryFilter.Matches(row, One("build", EgiFilterOp.NotContains, "36.0")));
        Assert.True(RegistryFilter.Matches(row, One("build", EgiFilterOp.StartsWith, "1.36")));
        Assert.False(RegistryFilter.Matches(row, One("build", EgiFilterOp.StartsWith, "36")));
        Assert.True(RegistryFilter.Matches(row, One("sha", EgiFilterOp.StartsWith, "ABCDEF")));
    }

    [Fact]
    public void VersionOrderingIsNumericNotLexicographic() {
        ProtoRegistryRow row = Row(app: "1.36.0");
        Assert.True(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.Greater, "1.11.0")));
        Assert.False(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.Less, "1.11.0")));
        Assert.True(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.AtLeast, "1.36.0")));
        Assert.True(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.AtMost, "1.36.0")));
        Assert.True(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.Is, "1.36.0")));
    }

    [Fact]
    public void AnUnknownVersionFailsOrderingAndPassesIsNot() {
        ProtoRegistryRow row = Row(app: "");
        Assert.False(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.Greater, "1.11.0")));
        Assert.False(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.Is, "1.11.0")));
        Assert.True(RegistryFilter.Matches(row, One("appVersion", EgiFilterOp.IsNot, "1.11.0")));
    }

    [Fact]
    public void NumberOperatorsRunOverSortOrder() {
        ProtoRegistryRow row = Row(order: 5);
        Assert.True(RegistryFilter.Matches(row, One("sortOrder", EgiFilterOp.Is, "5")));
        Assert.True(RegistryFilter.Matches(row, One("sortOrder", EgiFilterOp.Greater, "4")));
        Assert.True(RegistryFilter.Matches(row, One("sortOrder", EgiFilterOp.AtMost, "5")));
        Assert.False(RegistryFilter.Matches(row, One("sortOrder", EgiFilterOp.Less, "5")));
        Assert.False(RegistryFilter.Matches(Row(order: null), One("sortOrder", EgiFilterOp.Is, "5")));
        Assert.True(RegistryFilter.Matches(Row(order: null), One("sortOrder", EgiFilterOp.IsNot, "5")));
    }

    [Fact]
    public void DateOperatorsCompareTheLocalDate() {
        var stamp = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc);
        string day = stamp.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        ProtoRegistryRow row = Row(detected: stamp);
        Assert.True(RegistryFilter.Matches(row, One("detected", EgiFilterOp.On, day)));
        Assert.True(RegistryFilter.Matches(row, One("detected", EgiFilterOp.OnOrBefore, day)));
        Assert.True(RegistryFilter.Matches(row, One("detected", EgiFilterOp.OnOrAfter, day)));
        Assert.False(RegistryFilter.Matches(row, One("detected", EgiFilterOp.Before, day)));
        Assert.False(RegistryFilter.Matches(row, One("detected", EgiFilterOp.After, day)));
    }

    [Fact]
    public void ANullDetectedAtFailsEveryDateOperator() {
        ProtoRegistryRow row = Row(detected: null);
        foreach (EgiFilterOp op in new[] {
                     EgiFilterOp.On, EgiFilterOp.Before, EgiFilterOp.After, EgiFilterOp.OnOrBefore, EgiFilterOp.OnOrAfter
                 }) {
            Assert.False(RegistryFilter.Matches(row, One("detected", op, "2026-05-04")));
        }
    }

    [Fact]
    public void BoolFieldsReadStoredTextAndBadBuild() {
        Assert.True(RegistryFilter.Matches(Row(sha: "abc"), One("hasText", EgiFilterOp.True, "")));
        Assert.True(RegistryFilter.Matches(Row(sha: null), One("hasText", EgiFilterOp.False, "")));
        Assert.True(RegistryFilter.Matches(Row(flag: "short"), One("badBuild", EgiFilterOp.True, "")));
        Assert.True(RegistryFilter.Matches(Row(flag: null), One("badBuild", EgiFilterOp.False, "")));
        Assert.False(RegistryFilter.Matches(Row(flag: "short"), One("badBuild", EgiFilterOp.Contains, "short")));
    }

    [Fact]
    public void GroupsOrWhileConditionsAnd() {
        ProtoRegistryRow row = Row(app: "1.36.0", source: "device");
        var both = new ListQuery("", "", [
            new EgiFilterGroup([
                new EgiFilterCondition("appVersion", EgiFilterOp.Is, "1.36.0"),
                new EgiFilterCondition("source", EgiFilterOp.Is, "crawl")
            ])
        ]);
        Assert.False(RegistryFilter.Matches(row, both));

        var either = new ListQuery("", "", [
            new EgiFilterGroup([new EgiFilterCondition("source", EgiFilterOp.Is, "crawl")]),
            new EgiFilterGroup([new EgiFilterCondition("appVersion", EgiFilterOp.Is, "1.36.0")])
        ]);
        Assert.True(RegistryFilter.Matches(row, either));
    }

    [Fact]
    public void AnUnknownFieldKeyRejectsTheRow() {
        Assert.False(RegistryFilter.Matches(Row(), One("platform", EgiFilterOp.Is, "ios")));
        Assert.False(RegistryFilter.Matches(Row(), One("nonsense", EgiFilterOp.Is, "x")));
        Assert.Null(RegistryFilter.Field("platform"));
    }

    [Fact]
    public void PruneDropsIncompleteConditionsAndThenEmptyGroups() {
        var query = new ListQuery("", "", [
            new EgiFilterGroup([new EgiFilterCondition("appVersion", EgiFilterOp.Is, "")]),
            new EgiFilterGroup([
                new EgiFilterCondition("", EgiFilterOp.Is, "x"),
                new EgiFilterCondition("source", EgiFilterOp.Is, "device")
            ]),
            new EgiFilterGroup([new EgiFilterCondition("hasText", EgiFilterOp.True, "")])
        ]);

        ListQuery pruned = RegistryFilter.Prune(query);
        Assert.Equal(2, pruned.Groups.Count);
        Assert.Single(pruned.Groups[0].Conditions);
        Assert.Equal("source", pruned.Groups[0].Conditions[0].Field);
        Assert.Equal("hasText", pruned.Groups[1].Conditions[0].Field);
    }

    [Fact]
    public void SignatureIsStableAndSeparatesEveryPart() {
        var a = new ListQuery("ios", "1.36", [
            new EgiFilterGroup([new EgiFilterCondition("appVersion", EgiFilterOp.AtLeast, "1.36.0")])
        ]);
        var b = new ListQuery("ios", "1.36", [
            new EgiFilterGroup([new EgiFilterCondition("appVersion", EgiFilterOp.AtLeast, "1.36.0")])
        ]);
        Assert.Equal(a.Signature(), b.Signature());

        Assert.NotEqual(a.Signature(), (a with { Platform = "android" }).Signature());
        Assert.NotEqual(a.Signature(), (a with { Quick = "1.37" }).Signature());
        Assert.NotEqual(a.Signature(), (a with { Groups = [] }).Signature());
        Assert.NotEqual(
            a.Signature(),
            new ListQuery("ios", "1.36", [
                new EgiFilterGroup([new EgiFilterCondition("appVersion", EgiFilterOp.AtMost, "1.36.0")])
            ]).Signature());
        Assert.NotEqual(
            new ListQuery("", "ab", []).Signature(),
            new ListQuery("a", "b", []).Signature());
    }

    [Fact]
    public void VersionOptionsAreNewestFirstAndTextOptionsAreAscending() {
        ProtoRegistryRow[] rows = [
            Row(app: "1.11.0", build: "b1", source: "device"),
            Row(app: "1.36.0", build: "b3", source: "crawl"),
            Row(app: "1.36.0", build: "b2", source: "device")
        ];

        IReadOnlyList<EgiFilterOption> versions = RegistryFilter.Field("appVersion")!.Options!(rows);
        Assert.Equal(new[] { "1.36.0", "1.11.0" }, versions.Select(o => o.Value).ToArray());

        IReadOnlyList<EgiFilterOption> sources = RegistryFilter.Field("source")!.Options!(rows);
        Assert.Equal(new[] { "crawl", "device" }, sources.Select(o => o.Value).ToArray());
    }

    [Fact]
    public void OpLabelUsesTheFieldsOwnOperatorSet() {
        Assert.Equal("at least", RegistryFilter.OpLabel("appVersion", EgiFilterOp.AtLeast));
        Assert.Equal("does not contain", RegistryFilter.OpLabel("build", EgiFilterOp.NotContains));
        Assert.Equal("on or after", RegistryFilter.OpLabel("detected", EgiFilterOp.OnOrAfter));
    }
}
