using System.Text;
using System.Text.RegularExpressions;
using EggIdentity.Auth;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggSharp.GameData;
using EggIncognito.Models.Admin;
using EggIncognito.Models.AdminUi;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Assets;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Services.EggSharpSender;
using EggIncognito.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed partial class AdminController(
    ICurrentUser currentUser,
    IServiceProvider services,
    CaptureSessionManager sessions,
    GameDataStore gameDataStore)
    : ApiControllerBase {
    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex IconNameRegex();

    [HttpGet("users")]
    public async Task<IActionResult> Users([FromServices] IdentityApiClient? identity) {
        if (identity is null) return Fail(503, "identity api not configured");
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var rows = users.Select(u => new { u.DiscordId, u.Username, u.Role, u.Providers, u.LastLoginAt });
        return Ok(rows);
    }

    [HttpGet("api-keys")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ApiKeys([FromServices] ApiKeyStore? store, CancellationToken ct) {
        if (store is null) return Ok(new List<ApiKeyRow>());
        var rows = await store.AllAsync(ct);
        return Ok(rows.Select(k => new ApiKeyRow(k.Id, k.OwnerUserId.ToString(), k.Name, k.Prefix, k.LastUsedAt,
            k.RequestCount, k.Revoked)).ToList());
    }

    [HttpDelete("api-keys/{id:int}")]
    [RequiresDb]
    public async Task<IActionResult> RevokeApiKey(int id, [FromServices] ApiKeyStore store, CancellationToken ct) {
        bool ok = await store.AdminRevokeAsync(id, ct);
        if (!ok) return Fail(404, "key not found");
        return Ok(new { revoked = true });
    }

    [HttpDelete("api-keys/{id:int}/purge")]
    [RequiresDb]
    public async Task<IActionResult> DeleteApiKey(int id, [FromServices] ApiKeyStore store, CancellationToken ct) {
        bool ok = await store.AdminDeleteAsync(id, ct);
        if (!ok) return Fail(404, "key not found");
        return Ok(new { deleted = true });
    }

    [HttpGet("api-keys/features")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ApiKeyFeatures([FromServices] ApiKeyFeatureStore? grants,
        [FromServices] ApiKeyStore? keys, CancellationToken ct) {
        if (grants is null || keys is null) return Ok(new List<FeatureGrantRow>());
        var byId = (await keys.AllAsync(ct)).ToDictionary(k => k.Id);
        return Ok((await grants.AllAsync(ct))
            .Where(g => byId.ContainsKey(g.ApiKeyId))
            .Select(g => {
                var k = byId[g.ApiKeyId];
                return new FeatureGrantRow(g.Id, k.Id, k.Name, k.Prefix, k.OwnerUserId.ToString(), g.Feature, g.State,
                    g.Note, g.RequestedAt, g.DecidedAt, g.DecidedBy);
            })
            .ToList());
    }

    [HttpPost("api-keys/features/{grantId:int}/{decision}")]
    [RequiresDb]
    public async Task<IActionResult> DecideApiKeyFeature(int grantId, string decision,
        [FromServices] ApiKeyFeatureStore grants, [FromServices] ApiFeatureGrantCache cache, CancellationToken ct) {
        if (ApiKeyFeatureStates.ForDecision(decision) is not { } target)
            return Fail(400, "decision must be approve, deny or revoke");
        var me = currentUser.Current;
        string by = me.Name ?? me.Id?.ToString() ?? "admin";
        var (outcome, row) = await grants.DecideAsync(grantId, target, by, ct);
        if (outcome == GrantDecision.NotFound) return Fail(404, "grant not found");
        if (outcome == GrantDecision.Invalid) return Fail(409, $"grant is {row!.State}");
        cache.Invalidate(row!.ApiKeyId);
        return Ok(new { id = row.Id, state = row.State });
    }

    [HttpGet("sessions")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Sessions([FromServices] DeviceCaptureManager dcm,
        [FromServices] IDeviceFleet fleet, CancellationToken ct) {
        var rows = new List<SessionRow>();
        rows.AddRange(sessions.All().Select(x => SessionRow.FromCapture(x.Key, x.Session.Hub.StatsSnapshot())));
        rows.AddRange((await fleet.EnabledAsync(ct))
            .Select(d => SessionRow.FromDevice(d.Id, dcm.PortFor(d.Id), dcm.DiagFor(d.Id))));
        return Ok(rows);
    }

    [HttpGet("gamedata")]
    [EnableRateLimiting("read")]
    public IActionResult GameDataStatus([FromServices] DataCatalog dataCatalog, [FromServices] GameConfigStore configStore,
        [FromServices] IConfiguration cfg) {
        var families = new Dictionary<string, (int Count, string? Version, List<GameDataSourceRow> Sources)>(StringComparer.Ordinal);
        if (gameDataStore.Provider is { } provider) {
            foreach (var f in provider.Families)
                families[DocIdFor(f.Key)] = (f.Effects.Count, null, [.. f.Provenance.Select(s => new GameDataSourceRow(s.Key, s.Value.Origin, s.Value.Locator, s.Value.Method))]);

            string? route = dataCatalog.ById("periodical", "get_periodicals")?.WireRoute;
            var live = route is null ? null : LiveColleggtibleSource.Derive(services, route);
            families["colleggtibles"] = live is not null
                ? (live.Extract.Eggs.Count, live.GameVersion, [.. live.Provenance.Select(s => new GameDataSourceRow(s.Key, s.Value.Origin, s.Value.Locator, s.Value.Method))])
                : (provider.Colleggtibles.Eggs.Count, provider.Colleggtibles.GameVersion,
                    [.. provider.Colleggtibles.Provenance.Select(s => new GameDataSourceRow(s.Key, s.Value.Origin, s.Value.Locator, s.Value.Method))]);
        }

        var stored = gameDataStore.List().ToDictionary(d => d.Id, StringComparer.Ordinal);
        List<GameDataDocRow> documents = [
            .. GameDataProvider.ImportableIds.Select(id => {
                var doc = stored.GetValueOrDefault(id);
                bool hasFamily = families.TryGetValue(id, out var fam);
                return new GameDataDocRow(id, doc is not null, doc?.UpdatedAt, doc?.Bytes,
                    hasFamily && fam.Count > 0 ? fam.Count : null, hasFamily ? fam.Version : null,
                    hasFamily ? fam.Sources : []);
            })
        ];

        bool configEnabled = configStore.Enabled;
        List<DataStatusConfigPlatform> platforms = [
            .. configStore.List().Select(c => new DataStatusConfigPlatform(c.Platform, c.SavedAt, c.Bytes))
        ];

        var fixtures = new List<DataStatusFixtureRow>();
        string endpointsRoot = cfg["EndpointsPath"] ?? Path.Combine(AppContext.BaseDirectory, "Endpoints");
        string eiDir = Path.Combine(endpointsRoot, "default", "ei");
        if (Directory.Exists(eiDir)) {
            foreach (string path in Directory.EnumerateFiles(eiDir, "*.json")
                         .OrderBy(p => p, StringComparer.Ordinal)) {
                var info = new FileInfo(path);
                string status;
                try {
                    string trimmed = System.IO.File.ReadAllText(path).Trim();
                    status = trimmed.Length == 0 || trimmed == "{}" ? "empty" : "ok";
                } catch {
                    status = "unreadable";
                }

                fixtures.Add(new DataStatusFixtureRow(Path.GetFileNameWithoutExtension(info.Name), info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), status));
            }
        }

        return Ok(new GameDataStatusResponse(documents, [.. gameDataStore.MissingIds()],
            new DataStatusConfig(configEnabled, platforms), fixtures));
    }

    private static string DocIdFor(string familyKey) => familyKey switch {
        "boost" => "boosts",
        "research" => "research",
        "hab" => "habs",
        "artifact" => "artifacts",
        _ => familyKey
    };

    [HttpPost("gamedata/rebuild")]
    [RequiresDb]
    public async Task<IActionResult> RebuildGameDataDocuments([FromServices] GameDataRebuilder rebuilder,
        [FromQuery] bool force = true, CancellationToken ct = default) {
        (var results, string? binaryNote) = await rebuilder.RebuildAsync(force, ct);
        List<GameDataRebuildDocResult> rows =
            [.. results.Select(r => new GameDataRebuildDocResult(r.Id, r.Status, r.Count, r.Bytes, r.Note))];
        return Ok(new GameDataRebuildResponse(rows, binaryNote, [.. gameDataStore.MissingIds()]));
    }

    [HttpPost("eggsharp/send")]
    [RequiresDb]
    public async Task<IActionResult> SendToEggSharp([FromServices] EggSharpProtoSender protos,
        [FromServices] EggSharpDataPusher data, CancellationToken ct) {
        if (protos.Enabled) data.Request();
        ProtoSendOutcome proto;
        try {
            proto = await protos.SendLatestAsync(ct);
        } catch (HttpRequestException ex) {
            proto = new ProtoSendOutcome("failed", null, null, null, ex.Message);
        }
        return Ok(new EggSharpSendResponse(protos.Enabled, proto, protos.Enabled ? "queued" : "disabled", data.Last));
    }

    [HttpPost("protos/realign")]
    [RequiresDb]
    public async Task<IActionResult> RealignProtos([FromQuery] bool confirm,
        [FromServices] EggIncognitoDbContext db, CancellationToken ct) =>
        Ok(await ProtoRealignBackfill.RunAsync(db, !confirm, ct));

    [HttpPost("gamedata/{id}")]
    [RequestSizeLimit(2_000_000)]
    [RequiresDb]
    public async Task<IActionResult> ImportGameDataDocument(string id, [FromServices] EggIncognitoDbContext db,
        [FromServices] TimeProvider time, CancellationToken ct) {
        if (!GameDataProvider.ImportableIds.Contains(id)) return Fail(404, "unknown document id");

        string json;
        using (var reader = new StreamReader(Request.Body)) json = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return Fail(400, "empty body");

        try {
            GameDataProvider.Validate(id, json);
        } catch (GameDataSchemaException ex) {
            return Fail(400, ex.Message);
        }

        var now = time.GetUtcNow();
        await GameDataStore.UpsertAsync(db, id, json, null, now, ct);
        return Ok(new { id, bytes = Encoding.UTF8.GetByteCount(json), updatedAt = now });
    }

    [HttpGet("icons")]
    [EnableRateLimiting("read")]
    [RequiresDb]
    public async Task<IActionResult> Icons([FromServices] EggIncognitoDbContext db, CancellationToken ct) {
        var icons = await db.DeviceAssets.AsNoTracking()
            .Where(i => i.Kind == DeviceAssetKinds.Icon)
            .OrderBy(i => i.Name)
            .Select(i => new IconRow(i.Name, i.Platform, i.ByteSize, i.ContentType, i.Sha256, i.UpdatedAt))
            .ToArrayAsync(ct);
        return Ok(new IconList(icons));
    }

    [HttpPost("icons/{name}")]
    [RequestSizeLimit(1_000_000)]
    [RequiresDb]
    public async Task<IActionResult> ImportIcon(string name, [FromServices] EggIncognitoDbContext db,
        [FromServices] TimeProvider time, [FromServices] BlobBytes? blobs, CancellationToken ct) {
        if (!IconNameRegex().IsMatch(name)) return Fail(400, "invalid icon name");

        byte[] data;
        if (Request.HasFormContentType) {
            var form = await Request.ReadFormAsync(ct);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null) return Fail(400, "empty body");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            data = ms.ToArray();
        } else {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }

        if (data.Length == 0) return Fail(400, "empty body");
        bool png = data is [0x89, 0x50, 0x4E, 0x47, ..];
        if (!png) return Fail(400, "not a png");

        await new DeviceAssetStore(db, time, blobs ?? BlobBytes.Inline).PutAsync(DeviceAssetKinds.AnyPlatform,
            DeviceAssetKinds.Icon, name, data, "image/png", null, ct);
        return Ok(new { name, bytes = data.Length });
    }

    [HttpDelete("icons/{name}")]
    [RequiresDb]
    public async Task<IActionResult> DeleteIcon(string name, [FromServices] EggIncognitoDbContext db,
        CancellationToken ct) {
        if (!IconNameRegex().IsMatch(name)) return Fail(400, "invalid icon name");

        int removed = await db.DeviceAssets
            .Where(a => a.Kind == DeviceAssetKinds.Icon && a.Name == name)
            .ExecuteDeleteAsync(ct);
        if (removed == 0) return Fail(404, "icon not found");
        return Ok(new { deleted = name, rows = removed });
    }

    [HttpPost("icons/wipe")]
    [RequiresDb]
    public async Task<IActionResult> WipeIcons([FromServices] EggIncognitoDbContext db,
        [FromServices] IconAssetCache diskCache, [FromServices] IDeviceAgentClient agent, CancellationToken ct) {
        int rows = await db.DeviceAssets
            .Where(a => a.Kind == DeviceAssetKinds.Icon)
            .ExecuteDeleteAsync(ct);
        int fingerprints = await db.DeviceAssets
            .Where(a => a.Kind == DeviceAssetKinds.Manifest
                        && a.Name == DeviceAssetStore.FingerprintPrefix + HarvestEntries.Textures)
            .ExecuteDeleteAsync(ct);
        int files = diskCache.ClearAll();

        bool harvestStarted = agent.Enabled && await agent.PokeAsync(null, true, ct);
        string harvest = !agent.Enabled ? "device agent disabled"
            : harvestStarted ? "queued" : "no enabled devices";

        return Ok(new { rows, fingerprints, files, harvest });
    }

    [HttpDelete("sessions/{key}")]
    public async Task<IActionResult> KillSession(string key) {
        var session = sessions.Get(key);
        if (session is null) return Fail(404, "session not found");
        await session.StopAsync();
        if (key != CaptureSessionManager.LocalKey) sessions.Remove(key);
        return Ok(new { killed = key });
    }

    [HttpPost("users/{discordId}/role")]
    public async Task<IActionResult> SetUserRole(string discordId, [FromBody] SetRole body,
        [FromServices] IdentityApiClient? identity) {
        string role = (body.Role ?? "").Trim().ToLowerInvariant();
        if (UserRoles.ToName(UserRoles.Parse(role)) != role)
            return Fail(400, $"unknown role '{body.Role}'");
        if (discordId == currentUser.Current.DiscordId && role != UserRoles.ToName(UserRole.Admin))
            return Fail(400, "cannot remove your own admin role");

        if (identity is null) return Fail(503, "identity api not configured");
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var user = users.FirstOrDefault(u => u.DiscordId == discordId);
        if (user is null) return Fail(404, "user not found");
        await identity.SetRoleAsync(user.UserId, role, HttpContext.RequestAborted);
        return Ok(new { discordId, role });
    }

    [HttpPost("backfill-capture-user-ids")]
    [RequiresDb]
    public async Task<IActionResult> BackfillCaptureUserIds([FromServices] EggIncognitoDbContext db,
        [FromServices] IdentityApiClient? identity, CancellationToken ct) {
        if (identity is null) return Fail(503, "identity api not configured");
        int updated = await CaptureUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

    [HttpPost("backfill-owner-author-user-ids")]
    [RequiresDb]
    public async Task<IActionResult> BackfillOwnerAuthorUserIds([FromServices] EggIncognitoDbContext db,
        [FromServices] IdentityApiClient? identity, CancellationToken ct) {
        if (identity is null) return Fail(503, "identity api not configured");
        int updated = await OwnerAuthorUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

    [HttpPost("tag")]
    [RequiresDb]
    public async Task<IActionResult> AddTagAsync([FromBody] AddTag body, [FromServices] EggIncognitoDbContext db) {
        string slug = (body.Slug ?? "").Trim().ToLowerInvariant();
        string label = (body.Label ?? "").Trim();
        if (slug.Length == 0 || label.Length == 0) return Fail(400, "slug and label are required");

        if (await db.Tags.AnyAsync(t => t.Slug == slug)) return Fail(409, $"tag {slug} already exists");

        var tag = new Tag { Slug = slug, Label = label, Color = string.IsNullOrWhiteSpace(body.Color) ? null : body.Color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return Ok(new { tag.Id, tag.Slug, tag.Label, tag.Color });
    }

    [HttpDelete("tag/{id:long}")]
    [RequiresDb]
    public async Task<IActionResult> DeleteTag(long id, [FromServices] EggIncognitoDbContext db) {
        var tag = await db.Tags.FindAsync(id);
        if (tag is null) return NotFound();
        var joins = await db.SubjectTags.Where(s => s.TagId == id).ToListAsync();
        db.SubjectTags.RemoveRange(joins);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }
}
