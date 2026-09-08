using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EggIdentity.Client;
using EggIdentity.Contract;
using EggIncognito.Capture;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Models.Admin;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.DataApi;
using EggIncognito.Services.Devices;
using EggIncognito.Tools;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/admin")]
[ApiAccess(ApiAccessLevel.Admin)]
[EnableRateLimiting("write")]
public sealed partial class AdminController(ICurrentUser currentUser, IServiceProvider services) : ControllerBase {
    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex IconNameRegex();

    private static readonly JsonSerializerOptions ProvenanceJson = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private EggIncognitoDbContext? Db => services.GetService(typeof(EggIncognitoDbContext)) as EggIncognitoDbContext;
    private IdentityApiClient? Identity => services.GetService(typeof(IdentityApiClient)) as IdentityApiClient;

    private ApiKeyStore? Keys => services.GetService(typeof(ApiKeyStore)) as ApiKeyStore;

    private ObjectResult? RequireAdmin() =>
        currentUser.IsAtLeast(UserRole.Admin) ? null : StatusCode(403, new { error = "admin role required" });

    [HttpGet("users")]
    public async Task<IActionResult> Users() {
        if (RequireAdmin() is { } no) return no;
        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var rows = users.Select(u => new { u.DiscordId, u.Username, u.Role, u.Providers, u.LastLoginAt });
        return Ok(rows);
    }

    [HttpGet("api-keys")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> ApiKeys(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Keys;
        if (store is null) return Ok(new List<ApiKeyRow>());
        var rows = await store.AllAsync(ct);
        return Ok(rows.Select(k => new ApiKeyRow(k.Id, k.OwnerUserId.ToString(), k.Name, k.Prefix, k.LastUsedAt,
            k.RequestCount, k.Revoked)).ToList());
    }

    [HttpDelete("api-keys/{id:int}")]
    public async Task<IActionResult> RevokeApiKey(int id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Keys;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        bool ok = await store.AdminRevokeAsync(id, ct);
        if (!ok) return NotFound(new { error = "key not found" });
        return Ok(new { revoked = true });
    }

    [HttpDelete("api-keys/{id:int}/purge")]
    public async Task<IActionResult> DeleteApiKey(int id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var store = Keys;
        if (store is null) return StatusCode(503, new { error = "no database configured" });
        bool ok = await store.AdminDeleteAsync(id, ct);
        if (!ok) return NotFound(new { error = "key not found" });
        return Ok(new { deleted = true });
    }

    [HttpGet("sessions")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Sessions(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var rows = new List<object>();

        if (services.GetService(typeof(CaptureSessionManager))
            is CaptureSessionManager mgr)
            rows.AddRange(mgr.All().Select(x => {
                var s = x.Session.Hub.StatsSnapshot();
                return (object)new {
                    key = x.Key,
                    kind = x.Key == CaptureSessionManager.LocalKey ? "local" : "user",
                    killable = true,
                    running = s.Running,
                    port = s.Port,
                    flows = s.CapturedAuxbrain,
                    connections = s.ActiveConnections,
                    devices = s.DeviceCount,
                    decryptOk = s.DecryptOk,
                    decryptErr = s.DecryptErrors
                };
            }));

        if (services.GetService(typeof(DeviceCaptureManager))
                is DeviceCaptureManager dcm
            && services.GetService(typeof(IDeviceFleet))
                is IDeviceFleet fleet)
            rows.AddRange((await fleet.EnabledAsync(ct)).Select(d => {
                var diag = dcm.DiagFor(d.Id);
                int port = dcm.PortFor(d.Id);
                return (object)new {
                    key = $"device:{d.Id}",
                    kind = "device",
                    killable = false,
                    running = port != 0,
                    port,
                    flows = diag.Flows,
                    connections = diag.ClientConnects,
                    devices = 1,
                    decryptOk = diag.RinfoHarvests,
                    decryptErr = diag.LastDecryptError is null ? 0 : 1
                };
            }));

        return Ok(rows);
    }

    [HttpGet("data-status")]
    [EnableRateLimiting("read")]
    public IActionResult DataStatus() {
        if (RequireAdmin() is { } no) return no;

        var gameData = new List<object>();
        var gdStore = services.GetService(typeof(GameDataStore)) as GameDataStore;
        if (gdStore?.Provider is { } provider) {
            foreach (var f in provider.Families) {
                gameData.Add(new {
                    key = f.Key,
                    count = f.Effects.Count,
                    provenance = JsonSerializer.Serialize(f.Provenance, ProvenanceJson)
                });
            }

            string? route = (services.GetService(typeof(DataCatalog)) as DataCatalog)
                ?.ById("periodical", "get_periodicals")?.WireRoute;
            var live = route is null ? null : LiveColleggtibleSource.Derive(services, route);
            if (live is not null) {
                gameData.Add(new {
                    key = "colleggtibles",
                    count = live.Extract.Eggs.Count,
                    gameVersion = live.GameVersion,
                    provenance = JsonSerializer.Serialize(live.Provenance, ProvenanceJson)
                });
            } else {
                var col = provider.Colleggtibles;
                gameData.Add(new {
                    key = "colleggtibles",
                    count = col.Eggs.Count,
                    gameVersion = col.GameVersion,
                    provenance = JsonSerializer.Serialize(col.Provenance, ProvenanceJson)
                });
            }
        }

        object[] platforms = [];
        bool configEnabled = false;
        if (services.GetService(typeof(GameConfigStore)) is GameConfigStore store) {
            configEnabled = store.Enabled;
            platforms = [
                .. store.List().Select(c => (object)new { platform = c.Platform, savedAt = c.SavedAt, bytes = c.Bytes })
            ];
        }

        var fixtures = new List<object>();
        if (services.GetService(typeof(IConfiguration)) is IConfiguration cfg) {
            string eiDir = Path.Combine(ContentRoot.Resolve(cfg["ContentRoot"]), "Endpoints", "default", "ei");
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

                    fixtures.Add(new {
                        name = Path.GetFileNameWithoutExtension(info.Name),
                        bytes = info.Length,
                        updatedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                        status
                    });
                }
            }
        }

        var documents = gdStore?.List() ?? [];
        IReadOnlyList<string> missing = gdStore?.MissingIds() ?? [.. GameDataProvider.DocumentIds];
        return Ok(new { gameData, documents, missing, config = new { enabled = configEnabled, platforms }, fixtures });
    }

    [HttpGet("gamedata")]
    [EnableRateLimiting("read")]
    public IActionResult GameDataDocuments() {
        if (RequireAdmin() is { } no) return no;
        if (Db is null) return StatusCode(503, new { error = "no database configured" });
        var store = services.GetRequiredService<GameDataStore>();
        var rows = store.List().ToDictionary(d => d.Id, StringComparer.Ordinal);
        var documents = GameDataProvider.ImportableIds
            .Select(id => rows.TryGetValue(id, out var doc)
                ? new { id, present = true, updatedAt = (DateTimeOffset?)doc.UpdatedAt, bytes = (int?)doc.Bytes }
                : new { id, present = false, updatedAt = (DateTimeOffset?)null, bytes = (int?)null })
            .ToArray();
        return Ok(new { documents, missing = store.MissingIds() });
    }

    [HttpPost("gamedata/rebuild")]
    public async Task<IActionResult> RebuildGameDataDocuments([FromQuery] bool force = true,
        CancellationToken ct = default) {
        if (RequireAdmin() is { } no) return no;
        if (Db is null) return StatusCode(503, new { error = "no database configured" });
        var rebuilder = services.GetRequiredService<GameDataRebuilder>();
        (var results, string? binaryNote) = await rebuilder.RebuildAsync(force, ct);
        var store = services.GetRequiredService<GameDataStore>();
        return Ok(new { results, binary = binaryNote, missing = store.MissingIds() });
    }

    [HttpPost("protos/realign")]
    public async Task<IActionResult> RealignProtos([FromQuery] bool confirm, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        return Ok(await ProtoRealignBackfill.RunAsync(db, !confirm, ct));
    }

    [HttpPost("gamedata/{id}")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> ImportGameDataDocument(string id, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (!GameDataProvider.ImportableIds.Contains(id)) return NotFound(new { error = "unknown document id" });

        string json;
        using (var reader = new StreamReader(Request.Body)) json = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return BadRequest(new { error = "empty body" });

        try {
            GameDataProvider.Validate(id, json);
        } catch (GameDataSchemaException ex) {
            return BadRequest(new { error = ex.Message });
        }

        var now = DateTimeOffset.UtcNow;
        var row = await db.GameDataDocuments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) {
            db.GameDataDocuments.Add(new GameDataDocument { Id = id, Json = json, UpdatedAt = now });
        } else {
            row.Json = json;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { id, bytes = Encoding.UTF8.GetByteCount(json), updatedAt = now });
    }

    [HttpGet("icons")]
    [EnableRateLimiting("read")]
    public async Task<IActionResult> Icons(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var icons = await db.DeviceAssets.AsNoTracking()
            .Where(i => i.Kind == DeviceAssetKinds.Icon)
            .OrderBy(i => i.Name)
            .Select(i => new {
                name = i.Name,
                platform = i.Platform,
                bytes = i.ByteSize,
                contentType = i.ContentType,
                sha256 = i.Sha256,
                updatedAt = i.UpdatedAt
            })
            .ToListAsync(ct);
        return Ok(new { icons });
    }

    [HttpPost("icons/{name}")]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> ImportIcon(string name, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (!IconNameRegex().IsMatch(name)) return BadRequest(new { error = "invalid icon name" });

        byte[] data;
        if (Request.HasFormContentType) {
            var form = await Request.ReadFormAsync(ct);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file is null) return BadRequest(new { error = "empty body" });
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            data = ms.ToArray();
        } else {
            using var ms = new MemoryStream();
            await Request.Body.CopyToAsync(ms, ct);
            data = ms.ToArray();
        }

        if (data.Length == 0) return BadRequest(new { error = "empty body" });
        bool png = data.Length >= 8
                   && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
                   && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
        if (!png) return BadRequest(new { error = "not a png" });

        await new DeviceAssetStore(db).PutAsync(DeviceAssetKinds.AnyPlatform, DeviceAssetKinds.Icon, name, data,
            "image/png", null, ct);
        return Ok(new { name, bytes = data.Length });
    }

    [HttpDelete("icons/{name}")]
    public async Task<IActionResult> DeleteIcon(string name, CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (!IconNameRegex().IsMatch(name)) return BadRequest(new { error = "invalid icon name" });

        int removed = await db.DeviceAssets
            .Where(a => a.Kind == DeviceAssetKinds.Icon && a.Name == name)
            .ExecuteDeleteAsync(ct);
        if (removed == 0) return NotFound(new { error = "icon not found" });
        return Ok(new { deleted = name, rows = removed });
    }

    [HttpDelete("sessions/{key}")]
    public async Task<IActionResult> KillSession(string key) {
        if (RequireAdmin() is { } no) return no;
        if (services.GetService(typeof(CaptureSessionManager)) is not CaptureSessionManager mgr)
            return StatusCode(503, new { error = "capture not configured" });
        var session = mgr.Get(key);
        if (session is null) return NotFound(new { error = "session not found" });
        await session.StopAsync();
        if (key != CaptureSessionManager.LocalKey) mgr.Remove(key);
        return Ok(new { killed = key });
    }

    [HttpPost("users/{discordId}/role")]
    public async Task<IActionResult> SetUserRole(string discordId, [FromBody] SetRole body) {
        if (RequireAdmin() is { } no) return no;
        string role = (body.Role ?? "").Trim().ToLowerInvariant();
        if (UserRoles.ToName(UserRoles.Parse(role)) != role)
            return BadRequest(new { error = $"unknown role '{body.Role}'" });
        if (discordId == currentUser.DiscordId && role != UserRoles.ToName(UserRole.Admin))
            return BadRequest(new { error = "cannot remove your own admin role" });

        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        var users = await identity.ListAdminUsersAsync(HttpContext.RequestAborted);
        var user = users.FirstOrDefault(u => u.DiscordId == discordId);
        if (user is null) return NotFound(new { error = "user not found" });
        await identity.SetRoleAsync(user.UserId, role, HttpContext.RequestAborted);
        return Ok(new { discordId, role });
    }

    [HttpPost("backfill-capture-user-ids")]
    public async Task<IActionResult> BackfillCaptureUserIds(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        int updated = await CaptureUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

    [HttpPost("backfill-owner-author-user-ids")]
    public async Task<IActionResult> BackfillOwnerAuthorUserIds(CancellationToken ct) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var identity = Identity;
        if (identity is null) return StatusCode(503, new { error = "identity api not configured" });
        int updated = await OwnerAuthorUserIdBackfill.RunAsync(db, identity, ct);
        return Ok(new { updated });
    }

    [HttpPost("tag")]
    public async Task<IActionResult> AddTagAsync([FromBody] AddTag body) {
        if (RequireAdmin() is { } no) return no;
        string slug = (body.Slug ?? "").Trim().ToLowerInvariant();
        string label = (body.Label ?? "").Trim();
        if (slug.Length == 0 || label.Length == 0) return BadRequest(new { error = "slug and label are required" });

        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        if (await db.Tags.AnyAsync(t => t.Slug == slug)) return Conflict(new { error = $"tag {slug} already exists" });

        var tag = new Tag { Slug = slug, Label = label, Color = string.IsNullOrWhiteSpace(body.Color) ? null : body.Color };
        db.Tags.Add(tag);
        await db.SaveChangesAsync();
        return Ok(new { tag.Id, tag.Slug, tag.Label, tag.Color });
    }

    [HttpDelete("tag/{id:long}")]
    public async Task<IActionResult> DeleteTag(long id) {
        if (RequireAdmin() is { } no) return no;
        var db = Db;
        if (db is null) return StatusCode(503, new { error = "no database configured" });
        var tag = await db.Tags.FindAsync(id);
        if (tag is null) return NotFound();
        var joins = await db.SubjectTags.Where(s => s.TagId == id).ToListAsync();
        db.SubjectTags.RemoveRange(joins);
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return Ok(new { deleted = id });
    }
}
