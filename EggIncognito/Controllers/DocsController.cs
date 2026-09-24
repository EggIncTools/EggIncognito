using System.Collections.Frozen;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.Models.Docs;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using EggIncognito.Services.Inspector;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/docs")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class DocsController(ICurrentUser currentUser, TimeProvider time) : ApiControllerBase {
    private const int MaxImageBytes = 4 * 1024 * 1024;

    private static readonly FrozenSet<string> AllowedImageTypes =
        FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "image/png", "image/jpeg", "image/gif", "image/webp");

    private static bool ValidKind(string kind) => DocSubjectKinds.IsKnown(kind);

    private static bool TaggableKind(string kind) => kind == DocSubjectKinds.Endpoint;

    private void CacheFor(int seconds) =>
        Response.Headers.CacheControl = $"private, max-age={seconds}";

    [HttpGet("doc/{kind}/{**key}")]
    public async Task<IActionResult> GetDoc(string kind, string key, [FromServices] EggIncognitoDbContext? db) {
        if (!ValidKind(kind)) return Fail(400, "invalid subject kind");
        if (db is null) return Ok(new DocResult(null));
        var doc = await db.Docs.AsNoTracking()
            .FirstOrDefaultAsync(d => d.SubjectKind == kind && d.SubjectKey == key);
        return Ok(doc is null ? new DocResult(null) : new DocResult(doc.BodyMd, doc.UpdatedAt));
    }

    [HttpPost("doc")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> UpsertDocAsync([FromBody] UpsertDoc body, [FromServices] EggIncognitoDbContext db) {
        if (!ValidKind(body.SubjectKind)) return Fail(400, "invalid subject kind");

        var existing = await db.Docs
            .FirstOrDefaultAsync(d => d.SubjectKind == body.SubjectKind && d.SubjectKey == body.SubjectKey);
        bool empty = string.IsNullOrWhiteSpace(body.BodyMd);

        if (existing is null) {
            if (empty) return Ok(new { saved = false });
            db.Docs.Add(new Doc {
                SubjectKind = body.SubjectKind,
                SubjectKey = body.SubjectKey,
                BodyMd = body.BodyMd,
                OwnerUserId = currentUser.UserId
            });
        } else if (empty) {
            db.Docs.Remove(existing);
        } else {
            existing.BodyMd = body.BodyMd;
            existing.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync();
        return Ok(new { saved = !empty });
    }

    [HttpGet("tags")]
    public async Task<IActionResult> GetTags([FromServices] EggIncognitoDbContext? db) {
        if (db is null) return Ok(new List<TagRow>());
        var rows = await db.Tags.AsNoTracking().OrderBy(t => t.Label)
            .Select(t => new TagRow(t.Id, t.Slug, t.Label, t.Color)).ToListAsync();
        CacheFor(30);
        return Ok(rows);
    }

    [HttpGet("subject-tags/{kind}/{**key}")]
    public async Task<IActionResult> GetSubjectTags(string kind, string key, [FromServices] EggIncognitoDbContext? db) {
        if (!TaggableKind(kind)) return Fail(400, "tags apply to endpoints only");
        if (db is null) return Ok(new List<TagRow>());
        var rows = await (
            from st in db.SubjectTags.AsNoTracking()
            where st.SubjectKind == kind && st.SubjectKey == key
            join t in db.Tags.AsNoTracking() on st.TagId equals t.Id
            orderby t.Label
            select new TagRow(t.Id, t.Slug, t.Label, t.Color)).ToListAsync();
        CacheFor(30);
        return Ok(rows);
    }

    [HttpPost("subject-tags")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> SetSubjectTagsAsync([FromBody] SetSubjectTags body,
        [FromServices] EggIncognitoDbContext db) {
        if (!TaggableKind(body.SubjectKind)) return Fail(400, "tags apply to endpoints only");

        var wanted = (body.TagIds ?? []).Distinct().ToHashSet();

        if (wanted.Count > 0) {
            var real = await db.Tags.Where(t => wanted.Contains(t.Id)).Select(t => t.Id).ToListAsync();
            wanted = [.. real];
        }

        var current = await db.SubjectTags
            .Where(s => s.SubjectKind == body.SubjectKind && s.SubjectKey == body.SubjectKey)
            .ToListAsync();
        var currentIds = current.Select(s => s.TagId).ToHashSet();

        db.SubjectTags.RemoveRange(current.Where(s => !wanted.Contains(s.TagId)));
        foreach (long id in wanted.Where(id => !currentIds.Contains(id)))
            db.SubjectTags.Add(new SubjectTag { SubjectKind = body.SubjectKind, SubjectKey = body.SubjectKey, TagId = id });

        await db.SaveChangesAsync();
        return Ok(new { tagIds = wanted });
    }

    [HttpGet("tags-map")]
    public async Task<IActionResult> GetTagsMap([FromServices] EggIncognitoDbContext? db) {
        if (db is null) return Ok(new Dictionary<string, List<TagRow>>());
        var rows = await (
            from st in db.SubjectTags.AsNoTracking()
            where st.SubjectKind == DocSubjectKinds.Endpoint
            join t in db.Tags.AsNoTracking() on st.TagId equals t.Id
            select new { st.SubjectKind, st.SubjectKey, t.Id, t.Slug, t.Label, t.Color }).ToListAsync();

        var map = rows
            .GroupBy(r => $"{r.SubjectKind}:{r.SubjectKey}")
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Label)
                    .Select(r => new TagRow(r.Id, r.Slug, r.Label, r.Color)).ToList());
        CacheFor(30);
        return Ok(map);
    }

    internal static bool MagicMatches(byte[] b, string contentType) => contentType.ToLowerInvariant() switch {
        "image/png" => b.Length >= 8
                       && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                       && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A,
        "image/jpeg" => b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF,
        "image/gif" => b.Length >= 6
                       && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F'
                       && b[3] == (byte)'8' && (b[4] == (byte)'7' || b[4] == (byte)'9') && b[5] == (byte)'a',
        "image/webp" => b.Length >= 12
                        && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
                        && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P',
        _ => false
    };

    [HttpPost("image")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    [RequiresDb]
    public async Task<IActionResult> UploadImageAsync(IFormFile? file, [FromServices] EggIncognitoDbContext db) {
        if (file is not { Length: > 0 }) return Fail(400, "no file");
        if (file.Length > MaxImageBytes)
            return Fail(400, $"image exceeds {MaxImageBytes / (1024 * 1024)} MB");
        string ct = file.ContentType ?? "";
        if (!AllowedImageTypes.Contains(ct)) return Fail(400, $"unsupported content type '{ct}'");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();
        if (!MagicMatches(bytes, ct))
            return Fail(400, $"file bytes do not match the declared type '{ct}'");

        var img = new DocImage {
            ContentType = ct,
            Bytes = bytes,
            ByteSize = bytes.Length,
            OwnerUserId = currentUser.UserId
        };
        db.DocImages.Add(img);
        await db.SaveChangesAsync();
        return Ok(new { id = img.Id, url = $"/api/docs/image/{img.Id}" });
    }

    [HttpGet("image/{id:long}")]
    public async Task<IActionResult> GetImage(long id, [FromServices] EggIncognitoDbContext? db) {
        if (db is null) return NotFound();
        var img = await db.DocImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
        if (img is null) return NotFound();

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(img.Bytes, img.ContentType);
    }

    [HttpGet("has")]
    public async Task<IActionResult> GetHasDocs([FromServices] EggIncognitoDbContext? db) {
        if (db is null) return Ok(new Dictionary<string, bool>());
        var keys = await db.Docs.AsNoTracking()
            .Select(d => new { d.SubjectKind, d.SubjectKey }).ToListAsync();
        var map = keys.ToDictionary(k => $"{k.SubjectKind}:{k.SubjectKey}", _ => true);
        CacheFor(30);
        return Ok(map);
    }
}
