using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Services.Assets;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Services.DataApi;

public sealed class DataCatalog {
    public const string PeriodicalsRoute = "ei/get_periodicals";
    internal const string ConfigRoute = "ei/get_config";
    internal const string AfxConfigRoute = "ei_afx/config";
    internal const string ShowcaseRoute = "ei/get_shell_showcase";

    private static readonly JsonSerializerOptions SnakeJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions IndentedJson = new() {
        WriteIndented = true
    };

    private readonly Dictionary<string, DataSource> _byRoute;

    public DataCatalog() {
        Sources = Build();
        _byRoute = Sources
            .Where(s => s.WireRoute is not null && s.Provenance == DataProvenance.WireFixture)
            .ToDictionary(s => s.WireRoute!, StringComparer.Ordinal);
    }

    public IReadOnlyList<DataSource> Sources { get; }

    public DataSource? ById(string group, string id) =>
        Sources.FirstOrDefault(s =>
            string.Equals(s.Group, group, StringComparison.Ordinal) &&
            string.Equals(s.Id, id, StringComparison.Ordinal));

    public DataSource? ByChild(string group, string parentId, string subId) =>
        Sources.FirstOrDefault(s =>
            string.Equals(s.Group, group, StringComparison.Ordinal) &&
            string.Equals(s.Extends, parentId, StringComparison.Ordinal) &&
            string.Equals(s.Id, subId, StringComparison.Ordinal));

    public IReadOnlyList<DataSource> Children(DataSource parent) =>
        Sources.Where(s => string.Equals(s.Extends, parent.Id, StringComparison.Ordinal)).ToArray();

    public DataSource? ByWireRoute(string route) => _byRoute.GetValueOrDefault(route);

    public IReadOnlyList<DataSource> ByGroup(string group) =>
        Sources.Where(s => string.Equals(s.Group, group, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<DataSource> EgressSources() =>
        Sources.Where(s => s.Refresh.Egress && s.WireRoute is not null).ToArray();

    public IReadOnlyList<string> FeedWireRoutes() =>
        Sources
            .Where(s => s.Feed is not null && s.WireRoute is not null)
            .Select(s => s.WireRoute!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> WireRoutes() =>
        Sources
            .Where(s => s.WireRoute is not null && s.Provenance == DataProvenance.WireFixture)
            .Select(s => s.WireRoute!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public IReadOnlyList<string> PeriodicalFeeds() =>
        Sources
            .Where(s => string.Equals(s.Group, "periodical", StringComparison.Ordinal) && s.Feed is not null)
            .Select(s => s.Feed!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    public string UrlFor(DataSource s) => s.Extends is null
        ? $"/api/v1/data/{s.Group}/{s.Id}"
        : $"/api/v1/data/{s.Group}/{s.Extends}/{s.Id}";

    public static string EgressUrl(DataSource s) => $"{AuxbrainHosts.Origin}/{s.WireRoute}";

    private static IReadOnlyList<DataSource> Build() => [
        Wire("get_periodicals", "Periodicals",
            "The get_periodicals response with current events, contracts and custom eggs.",
            PeriodicalsRoute, ConfigFeeds.Periodicals,
            p => new GetPeriodicalsRequest { Rinfo = new BasicRequestInfo { Platform = p } }.ToByteArray()),
        Wire("afx-config", "Artifacts config",
            "The ei_afx/config response with artifact and mission parameters.",
            AfxConfigRoute, ConfigFeeds.Afx,
            p => new ArtifactsConfigurationRequest { Rinfo = new BasicRequestInfo { Platform = p } }.ToByteArray()),
        Wire("season-infos", "Season infos",
            "The get_season_infos_v2 response with contract season details.",
            "ei_ctx/get_season_infos_v2", ConfigFeeds.Seasons,
            p => new BasicRequestInfo { Platform = p }.ToByteArray(), listed: false),
        Wire("config", "Game config",
            "The get_config response with the DLC catalog and boost prices.",
            ConfigRoute, ConfigFeeds.Config,
            p => new ConfigRequest { Rinfo = new BasicRequestInfo { Platform = p } }.ToByteArray()),

        new("colleggtibles", "periodical", "Colleggtibles",
            "Custom egg buffs from the get_periodicals response.",
            DataProvenance.DerivedExtract, DataAccess.Public,
            null, null, new DataRefresh(false), false,
            ProduceColleggtibles,
            Extends: "get_periodicals"),
        new("eiafx", "periodical", "eiafx data",
            "Artifact families and tiers from the ei_afx/config response, with icons from get_config.",
            DataProvenance.DerivedExtract, DataAccess.Public,
            AfxConfigRoute, null, new DataRefresh(false), false,
            ProduceEiAfx, Extends: "afx-config"),

        DlcSlice("items", "items", "DLC items", "DLC store items from the get_config dlcCatalog."),
        DlcSlice("shells", "shells", "Shells", "Shells from the get_config dlcCatalog."),
        DlcSlice("shell-sets", "shellSets", "Shell sets", "Shell sets from the get_config dlcCatalog."),
        DlcSlice("shell-objects", "shellObjects", "Shell objects", "Shell objects from the get_config dlcCatalog."),
        DlcSlice("shell-groups", "shellGroups", "Shell groups", "Shell groups from the get_config dlcCatalog."),
        DlcSlice("decorators", "decorators", "Decorators", "Decorator sets from the get_config dlcCatalog."),

        Derived("boost-catalog", "Boost catalog",
            "Every boost with its price, effect and duration, from the game binary and get_config.",
            (ctx, _) => Task.FromResult(BoostCatalogPayload(ctx.Services))),
        Derived("artifact-catalog", "Artifact catalog",
            "Artifact quality, value, crafting price and XP by tier and rarity, from ei_afx/config.",
            (ctx, _) => Task.FromResult(DocJson(ctx.Services, ArtifactCatalog.DocumentId))),
        Derived("mission", "Missions", "Home screen mission goals from the game binary.",
            (ctx, _) => Task.FromResult(DocJson(ctx.Services, "missions"))),
        Derived("research-common", "Common research",
            "Common research lines from the game binary.",
            (ctx, _) => Task.FromResult(ResearchPayload(ctx.Services, epic: false))),
        Derived("research-epic", "Epic research",
            "Epic research lines from the game binary.",
            (ctx, _) => Task.FromResult(ResearchPayload(ctx.Services, epic: true))),

        new("artifact-consume", "observation", "Artifact consume observations",
            "Observed artifact consume results: byproducts, golden egg returns and craft rarities.",
            DataProvenance.Database, DataAccess.Authenticated,
            null, null, new DataRefresh(false), false,
            ArtifactObservationSource.ProduceAsync, Listed: false),

        new("icon", "asset", "Game icon", "Boost and artifact icon PNG by asset name.",
            DataProvenance.Asset, DataAccess.Public, null, null, new DataRefresh(false), true, ProduceIcon),

        new("event-icon", "asset", "Event icon",
            "Event icon PNG by event type.",
            DataProvenance.Asset, DataAccess.Public, null, null, new DataRefresh(false), true, ProduceEventIcon)
    ];

    private static DataSource Wire(string id, string display, string desc, string route, string? feed,
        Func<string, byte[]> egressRequest, bool listed = true) =>
        new(id, "periodical", display, desc, DataProvenance.WireFixture, DataAccess.Authenticated,
            route, feed, new DataRefresh(true), false,
            (ctx, _) => Task.FromResult(FixtureJson(ctx.Services, route)), egressRequest, Listed: listed);

    private static DataSource Derived(string id, string display, string desc,
        Func<DataProduceContext, CancellationToken, Task<DataPayload?>> produce) =>
        new(id, "gamedata", display, desc, DataProvenance.Database, DataAccess.Public,
            null, null, new DataRefresh(false), false, produce);

    private static DataSource DlcSlice(string id, string field, string display, string desc) =>
        new(id, "periodical", display, desc, DataProvenance.DerivedExtract, DataAccess.Public,
            null, null, new DataRefresh(false), false,
            (ctx, _) => Task.FromResult(ctx.Services.GetRequiredService<ConfigSliceCache>()
                .Slice(ctx.Services, ConfigRoute, field)),
            Extends: "config");

    private static string DefaultsDir(IServiceProvider services) {
        var config = services.GetRequiredService<IConfiguration>();
        string root = ContentRoot.Resolve(config["ContentRoot"]);
        return Path.Combine(root, "Endpoints", "default");
    }

    internal static string FixturePath(IServiceProvider services, string route) {
        string[] parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir(services), Path.Combine(parts[..^1]), file);
    }

    private static DataPayload? FixtureJson(IServiceProvider services, string route) {
        string? stored = StoredJson(services, route);
        if (stored is not null) return DataPayload.Json(stored);
        string path = FixturePath(services, route);
        return File.Exists(path) ? DataPayload.Json(File.ReadAllText(path)) : null;
    }

    internal static string? FixtureText(IServiceProvider services, string route) {
        string? stored = StoredJson(services, route);
        if (stored is not null) return stored;
        string path = FixturePath(services, route);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string? StoredJson(IServiceProvider services, string route) {
        if (services.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return null;
        try {
            return db.StoredEndpoints
                .FirstOrDefault(e => e.Path == route && e.Eid == null)?.ResponseJson;
        } catch {
            return null;
        }
    }

    private static string? DocText(IServiceProvider services, string id) =>
        services.GetService<GameDataStore>()?.Doc(id);

    private static DataPayload? DocJson(IServiceProvider services, string id) {
        string? text = DocText(services, id);
        return text is null ? null : DataPayload.Json(text);
    }

    private static DataPayload? BoostCatalogPayload(IServiceProvider services) {
        string? catalogText = DocText(services, "boost-catalog");
        string? boostsText = DocText(services, "boosts");
        if (catalogText is null && boostsText is null) return null;

        var catalogDoc = catalogText is null ? null : JsonNode.Parse(catalogText)!.AsObject();
        var boostsDoc = boostsText is null ? null : JsonNode.Parse(boostsText)!.AsObject();

        var identity = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var effects = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var order = new List<string>();

        if (catalogDoc?["boosts"] is JsonArray catalogBoosts) {
            foreach (var node in catalogBoosts) {
                var row = node!.AsObject();
                string id = row["id"]!.GetValue<string>();
                identity[id] = row;
                order.Add(id);
            }
        }

        if (boostsDoc?["rows"] is JsonArray boostRows) {
            foreach (var node in boostRows) {
                var row = node!.AsObject();
                string id = row["id"]!.GetValue<string>();
                effects[id] = row;
                if (!identity.ContainsKey(id)) order.Add(id);
            }
        }

        var boosts = new JsonArray();
        foreach (string id in order) {
            var merged = new JsonObject { ["id"] = id };
            identity.TryGetValue(id, out var idRow);
            effects.TryGetValue(id, out var fxRow);
            var meta = fxRow?["meta"]?.AsObject();
            CopyField(merged, "displayName", idRow);
            CopyField(merged, "price", idRow, meta);
            CopyField(merged, "tokenPrice", idRow, meta);
            CopyField(merged, "seRequired", idRow, meta);
            CopyField(merged, "iconAsset", idRow, meta);
            CopyField(merged, "target", fxRow);
            CopyField(merged, "combineMode", fxRow);
            CopyField(merged, "magnitude", fxRow);
            CopyField(merged, "kind", meta);
            CopyField(merged, "durationSeconds", meta);
            boosts.Add(merged);
        }

        var provenance = new JsonObject();
        if (boostsDoc?["provenance"] is JsonObject boostsProv) {
            foreach ((string key, var value) in boostsProv) provenance[key] = value?.DeepClone();
        }

        if (catalogDoc?["provenance"] is JsonObject catalogProv) {
            foreach ((string key, var value) in catalogProv) provenance[key] = value?.DeepClone();
        }

        var output = new JsonObject {
            ["boosts"] = boosts,
            ["binaryVersion"] = (catalogDoc?["binaryVersion"] ?? boostsDoc?["binaryVersion"])?.DeepClone(),
            ["provenance"] = provenance
        };
        return DataPayload.Json(output.ToJsonString(IndentedJson));
    }

    private static void CopyField(JsonObject target, string key, params JsonObject?[] sources) {
        foreach (var source in sources) {
            if (source is not null && source.TryGetPropertyValue(key, out var value) && value is not null) {
                target[key] = value.DeepClone();
                return;
            }
        }
    }

    private static DataPayload? ResearchPayload(IServiceProvider services, bool epic) {
        string? text = DocText(services, "research");
        if (text is null) return null;
        var doc = JsonNode.Parse(text)!.AsObject();
        var rows = new JsonArray();
        if (doc["rows"] is JsonArray all) {
            foreach (var node in all) {
                bool isEpic = node?["meta"]?["epic"]?.GetValue<bool>() ?? false;
                if (isEpic == epic) rows.Add(node!.DeepClone());
            }
        }

        var output = new JsonObject {
            ["rows"] = rows,
            ["binaryVersion"] = doc["binaryVersion"]?.DeepClone(),
            ["provenance"] = doc["provenance"]?.DeepClone()
        };
        return DataPayload.Json(output.ToJsonString(IndentedJson));
    }

    private static Task<DataPayload?> ProduceColleggtibles(DataProduceContext ctx, CancellationToken ct) {
        var live = LiveColleggtibleSource.Derive(ctx.Services, PeriodicalsRoute);
        return Task.FromResult(live is null ? null : DataPayload.Json(live.Json));
    }

    private static Task<DataPayload?> ProduceEiAfx(DataProduceContext ctx, CancellationToken ct) {
        string? afx = FixtureText(ctx.Services, AfxConfigRoute);
        if (afx is null) return Task.FromResult<DataPayload?>(null);
        string? configJson = FixtureText(ctx.Services, ConfigRoute);
        IReadOnlyDictionary<string, string> icons = new Dictionary<string, string>();
        if (configJson is not null) {
            try {
                icons = DlcArtifactIcons.FromConfigJson(configJson);
            } catch {
                icons = new Dictionary<string, string>();
            }
        }

        var data = EiAfxDataBuilder.BuildFromJson(afx, icons);
        return Task.FromResult<DataPayload?>(DataPayload.Json(JsonSerializer.Serialize(data, SnakeJson)));
    }

    private static async Task<DataPayload?> ProduceIcon(DataProduceContext ctx, CancellationToken ct) {
        string? name = ctx.Name;
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(['/', '\\', '.', ' ']) >= 0) return null;
        var assets = ctx.Services.GetRequiredService<GameAssetProvider>();
        var result = await assets.GetAsync(new GameAssetKey("icon", null, name), ct);
        return !result.Ok || result.Asset is null
            ? null
            : new DataPayload(result.Asset.Bytes, result.Asset.ContentType);
    }

    private static async Task<DataPayload?> ProduceEventIcon(DataProduceContext ctx, CancellationToken ct) {
        string? name = ctx.Name;
        if (string.IsNullOrEmpty(name)) return null;
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) return null;
        string stem = name.ToLowerInvariant().Replace('-', '_');
        var assets = ctx.Services.GetRequiredService<GameAssetProvider>();
        var glyph = await assets.GetCachedAsync(new GameAssetKey("icon", null, $"event_{stem}"), ct);
        if (!glyph.Ok || glyph.Asset is null) return null;
        bool ccOnly = ctx.Http.Request.Query["cc"] == "1";
        return DataPayload.Png(EventIconRenderer.Render(glyph.Asset.Bytes, name, ccOnly));
    }
}
