using EggIncognito.Controllers;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Feed;
using EggIncognito.Services.RateLimiting;

namespace EggIncognito.Tests;

public class DataApiRegistryTests {
    [Fact]
    public void Catalog_IdsUniquePerGroup_AndProducersPresent() {
        var c = new DataCatalog();
        Assert.NotEmpty(c.Sources);
        foreach (var g in c.Sources.GroupBy(s => s.Group))
            Assert.Equal(g.Count(), g.Select(s => s.Id).Distinct().Count());
        Assert.All(c.Sources, s => {
            Assert.False(string.IsNullOrWhiteSpace(s.Id));
            Assert.False(string.IsNullOrWhiteSpace(s.DisplayName));
            Assert.NotNull(s.Produce);
            Assert.StartsWith("/api/v1/data/", c.UrlFor(s));
        });
    }

    [Fact]
    public void Catalog_ByWireRoute_ResolvesFeed() {
        var c = new DataCatalog();
        Assert.Equal("periodicals", c.ByWireRoute("ei/get_periodicals")?.Feed);
        Assert.Equal("afx-config", c.ByWireRoute("ei_afx/config")?.Feed);
        Assert.Equal("season-infos", c.ByWireRoute("ei_ctx/get_season_infos_v2")?.Feed);
    }

    [Fact]
    public void Catalog_GamedataSources_MatchExpectedSet() {
        var c = new DataCatalog();
        string[] ids = [.. c.ByGroup("gamedata").Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal)];
        string[] expected =
            ["artifact-catalog", "boost-catalog", "mission", "research-common", "research-epic"];
        Assert.Equal(expected, ids);
        Assert.Equal("Common research", c.ById("gamedata", "research-common")?.DisplayName);
        Assert.Equal("Epic research", c.ById("gamedata", "research-epic")?.DisplayName);
    }

    [Fact]
    public void EveryFeature_IsRegistered() =>
        Assert.All(new DataCatalog().Sources.Where(s => s.Feature is not null),
            s => Assert.NotNull(ApiFeatures.Find(s.Feature!)));

    [Fact]
    public void ConsumeSources_AreGated() {
        var c = new DataCatalog();
        Assert.Equal(ApiFeatures.ConsumeObservations, c.ById("observation", "artifact-consume")?.Feature);
        var raw = c.ByChild("observation", "artifact-consume", "artifact-consume-raw");
        Assert.NotNull(raw);
        Assert.Equal(ApiFeatures.ConsumeObservations, raw.Feature);
    }

    [Fact]
    public void GatedSources_AreNeverProducedForTheIndex() =>
        Assert.All(new DataCatalog().Sources.Where(s => s.Feature is not null),
            s => Assert.True(DataApiController.SkipsProduce(s)));

    [Fact]
    public void Catalog_SeasonInfos_UnlistedButResolvable() {
        var c = new DataCatalog();
        var s = c.ById("periodical", "season-infos");
        Assert.NotNull(s);
        Assert.False(s.Listed);
        Assert.Equal("season-infos", s.Feed);
        Assert.True(s.Refresh.Egress);
        Assert.All(c.Sources.Where(x => x.Id is not "season-infos" and not "artifact-consume"), x => Assert.True(x.Listed));
    }

    [Fact]
    public void Catalog_EgressSources_HaveRouteAndRequestBuilder() {
        var c = new DataCatalog();
        Assert.NotEmpty(c.EgressSources());
        Assert.All(c.EgressSources(), s => {
            Assert.False(string.IsNullOrWhiteSpace(s.WireRoute));
            Assert.NotNull(s.BuildEgressRequest);
        });
    }

    [Fact]
    public void Catalog_PeriodicalFeeds_MatchFeedEventKinds() {
        var c = new DataCatalog();
        string[] feeds = [.. c.PeriodicalFeeds().OrderBy(x => x, StringComparer.Ordinal)];
        var feedField = EggIdentity.Filters.FilterFields.Find(
            FeedEventKinds.Config.Fields, EggIncognito.Services.Feed.Kinds.ConfigChangedKind.Feed);
        Assert.NotNull(feedField);
        string[] triggers = [.. feedField.Options.Select(o => o.Value).OrderBy(x => x, StringComparer.Ordinal)];
        Assert.Equal(triggers, feeds);
    }

    [Fact]
    public void Catalog_Feeds_MatchConfigFeedIds() {
        var c = new DataCatalog();
        string[] feeds = [.. c.PeriodicalFeeds().OrderBy(x => x, StringComparer.Ordinal)];
        string[] declared = [.. ConfigFeeds.All.Select(f => f.Id).OrderBy(x => x, StringComparer.Ordinal)];
        Assert.Equal(declared, feeds);
    }

    [Fact]
    public void Catalog_ConfigChildren_MatchExpectedSet() {
        var c = new DataCatalog();
        var config = c.ById("periodical", "config");
        Assert.NotNull(config);
        var children = c.Children(config);
        string[] ids = [.. children.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal)];
        string[] expected = [
            "decorators", "items", "shell-groups", "shell-objects", "shell-sets", "shells"
        ];
        Assert.Equal(expected, ids);
        Assert.All(children, s => {
            Assert.Equal(DataAccess.Public, s.Access);
            Assert.Equal(DataProvenance.DerivedExtract, s.Provenance);
            Assert.Null(s.Feed);
            Assert.False(s.Refresh.Egress);
            Assert.True(s.Listed);
        });
    }

    [Fact]
    public void Catalog_ByChild_ResolvesShellSetsUnderConfig() {
        var c = new DataCatalog();
        var src = c.ByChild("periodical", "config", "shell-sets");
        Assert.NotNull(src);
        Assert.Equal("/api/v1/data/periodical/config/shell-sets", c.UrlFor(src));
    }

    [Fact]
    public void RateLimitOptions_HasDataPolicies() {
        var o = RateLimitOptions.Defaults();
        Assert.True(o.Tiers.ContainsKey("Keyed"));
        Assert.True(o.Policies.ContainsKey("Data"));
        Assert.Equal(30, o.Policies["DataAnon"].PermitLimit);
        Assert.Equal(60, o.Policies["DataAnon"].WindowSeconds);
    }

    [Fact]
    public void ApiKeyGen_MintDeterministicHash() {
        (string full, string hash, string prefix) = ApiKeyGen.Mint();
        Assert.StartsWith("egi_live_", full);
        Assert.Equal(12, prefix.Length);
        Assert.Equal(hash, ApiKeyGen.HashOf(full));
        Assert.Equal(64, hash.Length);
        (string second, _, _) = ApiKeyGen.Mint();
        Assert.NotEqual(full, second);
    }
}
