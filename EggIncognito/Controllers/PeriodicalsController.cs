using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Assets;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Models.Periodicals;
using EggIncognito.Services;
using EggIncognito.Services.Assets;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Events;
using EggIncognito.Services.Periodicals;
using Ei;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/periodicals")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("read")]
public sealed class PeriodicalsController(
    ICurrentUser currentUser,
    IConfiguration config,
    DataCatalog catalog,
    IServiceProvider services,
    ILogger<PeriodicalsController> logger) : ControllerBase {
    private static readonly JsonSerializerOptions ProvenanceJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Dictionary<int, string> DimNames =
        ColleggtibleCatalog.DimensionCodes.ToDictionary(kv => kv.Value, kv => kv.Key);

    private string Root => ContentRoot.Resolve(config["ContentRoot"]);
    private string DefaultsDir => Path.Combine(Root, "Endpoints", "default");

    private IEnumerable<DataSource> WireSources =>
        catalog.ByGroup("periodical").Where(s => s.Provenance == DataProvenance.WireFixture);

    private ObjectResult? RequireAuthenticated() =>
        currentUser.IsAuthenticated ? null : StatusCode(401, new { error = "authentication required" });

    private static readonly (string DocId, Func<EffectDataFile, IEffectFamily> Factory)[] SummaryFamilies = [
        ("boosts", f => new BoostFamily(f)),
        ("research", f => new ResearchFamily(f)),
        ("habs", f => new HabFamily(f)),
        ("artifacts", f => new ArtifactFamily(f))
    ];

    [HttpGet("summary")]
    public IActionResult Summary() {
        var extracted = new List<object>();
        object? colleggtibles = null;
        if (services.GetService(typeof(GameDataStore)) is GameDataStore gdStore) {
            foreach (var (docId, factory) in SummaryFamilies) {
                if (gdStore.Doc(docId) is not { } json) continue;
                try {
                    var f = factory(EffectDataLoader.Parse(json));
                    extracted.Add(new {
                        key = f.Key,
                        count = f.Effects.Count,
                        binaryVersion = f.BinaryVersion,
                        provenance = JsonSerializer.Serialize(f.Provenance, ProvenanceJson)
                    });
                } catch (GameDataSchemaException ex) {
                    logger.LogWarning(ex, "periodicals summary: {DocId} document failed schema validation", docId);
                }
            }
        }

        string? liveRoute = catalog.ById("periodical", "get_periodicals")?.WireRoute;
        var live = liveRoute is null ? null : LiveColleggtibleSource.Derive(services, liveRoute);
        if (live is not null)
            colleggtibles = new {
                count = live.Identifiers.Count,
                gameVersion = live.GameVersion,
                provenance = JsonSerializer.Serialize(live.Provenance, ProvenanceJson),
                eggs = live.Identifiers.Select(id => {
                    var buffs = live.Extract.Eggs.FirstOrDefault(e => e.Identifier == id);
                    return new {
                        Identifier = id,
                        name = live.Names.GetValueOrDefault(id),
                        dimension = buffs is null ? "" : DimensionName(buffs.Dimension),
                        tierValues = buffs?.TierValues ?? [],
                        icon = live.Icons.GetValueOrDefault(id)
                    };
                })
            };

        object[] platforms = [];
        bool configEnabled = false;
        if (services.GetService(typeof(GameConfigStore)) is GameConfigStore store) {
            configEnabled = store.Enabled;
            platforms = [
                .. store.List().Select(c => (object)new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes })
            ];
        }

        return Ok(new {
            extracted,
            colleggtibles,
            config = new { enabled = configEnabled, platforms },
            feeds = WireSources.Select(FeedInfo).ToArray()
        });
    }

    [HttpGet("seasons")]
    public async Task<IActionResult> Seasons(CancellationToken ct) {
        string? route = catalog.ById("periodical", "season-infos")?.WireRoute;
        if (route is null) return NotFound(new { error = "season source missing" });
        string? seasonJson = await ResolveRouteJson(route, ct);
        if (seasonJson is null) return NotFound(new { error = "no season capture available" });

        ContractSeasonInfos infos;
        try {
            infos = ContractSeasonInfos.Parser.ParseJson(seasonJson);
        } catch (Exception ex) {
            return StatusCode(500, new { error = $"season fixture unreadable: {ex.Message}" });
        }

        var sightings = new List<EggSighting>();
        var icons = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? perRoute = catalog.ById("periodical", "get_periodicals")?.WireRoute;
        (string? perJson, _) = await ResolveCurrentJson(perRoute, ct);
        if (perJson is not null) {
            try {
                var per = PeriodicalsResponse.Parser.ParseJson(perJson);
                AddSightings(sightings, per.Contracts?.Contracts ?? []);
                foreach (var egg in per.Contracts?.CustomEggs ?? []) icons[egg.Identifier] = egg.Icon?.Url;
            } catch (InvalidProtocolBufferException ex) {
                logger.LogWarning(ex, "periodicals: colleggtible enrichment skipped, periodicals capture unreadable");
            }
        }

        string? infoJson = await ResolveRouteJson(ContractsInfoRoute, ct);
        if (infoJson is not null) {
            try {
                var info = ContractsInfoResponse.Parser.ParseJson(infoJson);
                AddSightings(sightings, info.Contracts);
                foreach (var egg in info.CustomEggs) icons.TryAdd(egg.Identifier, egg.Icon?.Url);
            } catch (InvalidProtocolBufferException ex) {
                logger.LogWarning(ex, "periodicals: contracts info fixture unreadable, colleggtible first-seen limited");
            }
        }

        if (services.GetService(typeof(EggIncognitoDbContext)) is EggIncognitoDbContext db) {
            try {
                var rows = await db.ContractReleases.AsNoTracking()
                    .Where(r => r.CustomEggId != null && r.CustomEggId != "")
                    .Select(r => new { r.CustomEggId, r.SeasonId, r.StartTime, r.Name, r.ContractId })
                    .ToListAsync(ct);
                foreach (var r in rows) {
                    sightings.Add(new EggSighting(r.CustomEggId!, r.SeasonId, UnixSeconds.FromTime(r.StartTime),
                        string.IsNullOrEmpty(r.Name) ? r.ContractId : r.Name));
                }
            } catch (Exception ex) {
                logger.LogWarning(ex, "periodicals: contract releases lookup failed, colleggtible first-seen limited");
            }
        }

        string[] seasonIds = [.. infos.Infos.Select(s => s.Id)];
        var seasonEggs = SeasonColleggtibles.Attribute(sightings, seasonIds, id => icons.GetValueOrDefault(id));
        return Ok(new { seasons = SeasonList(infos, seasonEggs) });
    }

    private const string ContractsInfoRoute = "ei_ctx/get_contracts_info";

    private static void AddSightings(List<EggSighting> sightings, IEnumerable<Contract> contracts) {
        foreach (var c in contracts) {
            if (string.IsNullOrEmpty(c.CustomEggId)) continue;
            double start = c.StartTime > 0 ? c.StartTime : c.ExpirationTime - c.LengthSeconds;
            sightings.Add(new EggSighting(c.CustomEggId, c.SeasonId, start,
                string.IsNullOrEmpty(c.Name) ? c.Identifier : c.Name));
        }
    }

    private static object[] SeasonList(ContractSeasonInfos infos, Dictionary<string, List<SeasonEgg>> seasonEggs) {
        var list = infos.Infos.ToList();
        double[] starts = ResolveStarts(list);
        double quarter = Quarter(starts);
        return [
            .. list.Select((s, i) => {
                object[] colleggtibles = seasonEggs.TryGetValue(s.Id, out var eggs)
                    ? [.. eggs.Select(e => (object)new { id = e.Id, icon = e.Icon, contracts = e.Contracts?.ToArray() ?? [] })]
                    : [];
                return (object)new {
                id = s.Id,
                name = string.IsNullOrEmpty(s.Name) ? PrettySeasonId(s.Id) : s.Name,
                startTime = starts[i],
                nextStartTime = i == 0 ? starts[0] + quarter : starts[i - 1],
                startDerived = !(s.HasStartTime && s.StartTime > 0),
                colleggtibles,
                gradeGoals = s.GradeGoals.Select(g => new {
                    grade = g.Grade.ToString(),
                    gradeIcon = $"/api/v1/data/asset/icon?name={RewardIconMap.GradeStem(g.Grade.ToString())}",
                    goals = g.Goals.Select(x => new {
                        cxp = x.Cxp,
                        rewardType = x.RewardType.ToString(),
                        rewardSubType = x.RewardSubType,
                        rewardAmount = x.RewardAmount,
                        icon = RewardIconMap.Stem(x.RewardType, x.RewardSubType) is { } stem
                            ? $"/api/v1/data/asset/icon?name={stem}"
                            : null
                    }).ToArray()
                }).ToArray()
                };
            })
        ];
    }

    private static double[] ResolveStarts(List<ContractSeasonInfo> list) {
        double[] starts = [.. list.Select(s => s.HasStartTime && s.StartTime > 0 ? s.StartTime : 0)];
        int[] known = [.. Enumerable.Range(0, starts.Length).Where(i => starts[i] > 0)];
        if (known.Length == 0) return starts;

        double quarter = Quarter(starts);
        for (int i = 0; i < starts.Length; i++) {
            if (starts[i] > 0) continue;
            int nearest = known.MinBy(j => Math.Abs(j - i));
            starts[i] = starts[nearest] - (i - nearest) * quarter;
        }

        return starts;
    }

    private static double Quarter(double[] starts) {
        int[] known = [.. Enumerable.Range(0, starts.Length).Where(i => starts[i] > 0)];
        double quarter = 7889400;
        if (known.Length >= 2) {
            double sum = 0;
            int n = 0;
            for (int k = 0; k + 1 < known.Length; k++) {
                int a = known[k], b = known[k + 1];
                if (starts[a] > starts[b] && b > a) {
                    sum += (starts[a] - starts[b]) / (b - a);
                    n++;
                }
            }

            if (n > 0) quarter = sum / n;
        }

        return quarter;
    }

    private static string PrettySeasonId(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));

    [HttpGet("feed/{name}")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> Feed(string name, CancellationToken ct) {
        if (RequireAuthenticated() is { } no) return no;
        var src = WireSources.FirstOrDefault(s => string.Equals(s.Feed, name, StringComparison.Ordinal));
        if (src is null) return NotFound(new { error = "unknown feed" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        return payload is null
            ? NotFound(new { error = "no capture on disk" })
            : Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("gamedata/{key}")]
    public async Task<IActionResult> GameData(string key, CancellationToken ct) {
        var src = catalog.ById("gamedata", key);
        if (src is null) return NotFound(new { error = "unknown dataset" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        return payload is null
            ? NotFound(new { error = "resource not found" })
            : Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("eiafx-data")]
    public async Task<IActionResult> EiAfxData(CancellationToken ct) {
        var src = catalog.ByChild("periodical", "afx-config", "eiafx");
        if (src is null) return NotFound(new { error = "eiafx source missing" });

        var payload = await src.Produce(new DataProduceContext(HttpContext, null), ct);
        return payload is null
            ? NotFound(new { error = "no ei_afx/config capture" })
            : Content(Encoding.UTF8.GetString(payload.Bytes), "application/json");
    }

    [HttpGet("current")]
    public async Task<IActionResult> Current(CancellationToken ct) {
        string? route = catalog.ById("periodical", "get_periodicals")?.WireRoute;
        (string? json, DateTimeOffset? capturedAt) = await ResolveCurrentJson(route, ct);
        if (json is null) return NotFound(new { error = "no periodicals capture available" });

        PeriodicalsResponse per;
        try {
            per = PeriodicalsResponse.Parser.ParseJson(json);
        } catch (Exception ex) {
            return StatusCode(500, new { error = $"periodicals capture unreadable: {ex.Message}" });
        }

        double? serverTime = per.Contracts is { HasServerTime: true } c ? c.ServerTime : null;
        var events = new List<object>();
        var iconCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var e in per.Events?.Events ?? []) {
            double? endTime = e.StartTime > 0 && e.Duration > 0
                ? e.StartTime + e.Duration
                : serverTime is { } st ? st + e.SecondsRemaining : null;
            events.Add(new {
                identifier = e.Identifier,
                type = e.Type,
                subtitle = e.Subtitle,
                multiplier = e.Multiplier,
                startTime = e.StartTime,
                duration = e.Duration,
                endTime,
                ccOnly = e.CcOnly,
                icon = await ResolveEventIcon(e.Type, iconCache, ct)
            });
        }

        return Ok(new { capturedAt, serverTime, events });
    }

    private async Task<string?> ResolveRouteJson(string route, CancellationToken ct) {
        if (services.GetService(typeof(EggIncognitoDbContext)) is EggIncognitoDbContext db) {
            try {
                var stored = await db.StoredEndpoints
                    .FirstOrDefaultAsync(s => s.Path == route && s.Eid == null, ct);
                if (stored is not null) return stored.ResponseJson;
            } catch (Exception ex) {
                logger.LogWarning(ex, "periodicals: stored endpoint lookup for {Route} failed, falling back to disk",
                    route);
            }
        }

        string path = FixturePath(route);
        return System.IO.File.Exists(path)
            ? await System.IO.File.ReadAllTextAsync(path, ct)
            : null;
    }

    private async Task<(string? Json, DateTimeOffset? CapturedAt)> ResolveCurrentJson(string? route, CancellationToken ct) {
        if (services.GetService(typeof(EggIncognitoDbContext)) is EggIncognitoDbContext db) {
            try {
                var snap = await db.PeriodicalsSnapshots
                    .OrderByDescending(s => s.CapturedAt)
                    .FirstOrDefaultAsync(ct);
                if (snap is not null) return (snap.ResponseJson, snap.CapturedAt);
                if (route is not null) {
                    var stored = await db.StoredEndpoints
                        .FirstOrDefaultAsync(s => s.Path == route && s.Eid == null, ct);
                    if (stored is not null) return (stored.ResponseJson, null);
                }
            } catch (Exception ex) {
                logger.LogWarning(ex, "periodicals: snapshot lookup failed, falling back to disk fixture");
            }
        }

        if (route is null) return (null, null);
        string path = FixturePath(route);
        return System.IO.File.Exists(path)
            ? (await System.IO.File.ReadAllTextAsync(path, ct), null)
            : (null, null);
    }

    private async Task<string?> ResolveEventIcon(string type, Dictionary<string, string?> cache, CancellationToken ct) {
        if (string.IsNullOrEmpty(type)) return null;
        if (cache.TryGetValue(type, out string? cached)) return cached;
        string? icon = null;
        if (services.GetService(typeof(GameAssetProvider)) is GameAssetProvider assets) {
            string stem = type.Replace('-', '_');
            string[] candidates = [$"event_{stem}", stem];
            foreach (string candidate in candidates) {
                var result = await assets.GetCachedAsync(new GameAssetKey("icon", null, candidate), ct);
                if (!result.Ok || result.Asset is null) continue;
                icon = $"/api/v1/data/asset/icon?name={Uri.EscapeDataString(candidate)}";
                break;
            }
        }

        cache[type] = icon;
        return icon;
    }

    private string FixturePath(string route) {
        string[] parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string file = parts[^1] + ".json";
        return Path.Combine(DefaultsDir, Path.Combine(parts[..^1]), file);
    }

    private static string DimensionName(int code) =>
        DimNames.TryGetValue(code, out string? n) ? n : code.ToString(CultureInfo.InvariantCulture);

    private object FeedInfo(DataSource src) {
        string route = src.WireRoute!;
        var file = new FileInfo(FixturePath(route));
        return new {
            name = src.Feed,
            path = route,
            present = file.Exists,
            bytes = file.Exists ? file.Length : 0,
            updatedAt = file.Exists ? file.LastWriteTimeUtc : (DateTime?)null
        };
    }
}
