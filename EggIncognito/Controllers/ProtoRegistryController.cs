using EggIdentity.Contract;
using EggIncognito.Core;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Core.Services.Protos;
using EggIncognito.Data.Services;
using EggIncognito.Models.Protos;
using EggIncognito.Services;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EggIncognito.Controllers;

[ApiController]
[Route("api/protos/versions")]
[ApiAccess(ApiAccessLevel.Public)]
[EnableRateLimiting("write")]
public sealed class ProtoRegistryController(IServiceProvider services, ICurrentUser user) : ApiControllerBase {
    private ProtoRegistryStore? Store => services.GetService<ProtoRegistryStore>();

    private StagedProtoStore? StagedStore => services.GetService<StagedProtoStore>();

    private ObjectResult? Require(UserRole role) =>
        user.IsAtLeast(role) ? null : Fail(403, $"{UserRoles.ToName(role)}+ only");

    [HttpPost]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> Save([FromBody] SaveRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return NoDb();
        if (string.IsNullOrWhiteSpace(req.Platform) || string.IsNullOrWhiteSpace(req.Build) ||
            string.IsNullOrWhiteSpace(req.AppVersion))
            return Fail(400, "platform, appVersion, build required");

        bool hasProto = !string.IsNullOrWhiteSpace(req.Proto);
        string? protoText = hasProto ? req.Proto : null;
        string sha = "";
        if (hasProto) {
            var norm = ProtoCanonicalForm.Normalize(req.Proto!);
            sha = norm.Ok ? norm.Sha! : ProtoHash.Of(req.Proto!);
        }

        var upsert = await store.UpsertAsync(
            req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package ?? "",
            sha, "", DateTimeOffset.UtcNow, user.Username, protoText,
            req.Source ?? "upload", ct: ct);
        return Ok(new {
            ok = true,
            created = upsert.Created,
            upsert.Row.Platform,
            upsert.Row.Build,
            protoSha = sha
        });
    }

    [HttpPatch("{platform}/{build}")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> Edit(string platform, string build, [FromBody] EditRequest req,
        CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return NoDb();
        var result = await store.UpdateMetadataAsync(
            platform, build, req.AppVersion, req.ClientVersion, req.Source, req.Build, ct);
        return result switch {
            ProtoRegistryStore.MetadataUpdate.Ok => Ok(new { ok = true }),
            ProtoRegistryStore.MetadataUpdate.BuildCollision =>
                Fail(409, $"build '{req.Build}' already exists for {platform}"),
            _ => NotFound()
        };
    }

    [HttpPost("{platform}/{build}/proto")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> SetProto(string platform, string build, [FromBody] SetProtoRequest req,
        CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (Store is not { } store) return NoDb();
        if (string.IsNullOrWhiteSpace(req.Proto)) return Fail(400, "proto required");
        var sha = await store.SetProtoAsync(platform, build, req.Proto, ct);
        return sha is null ? NotFound() : Ok(new { ok = true, protoSha = sha });
    }

    [HttpDelete("{platform}/{build}")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Delete(string platform, string build, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return NoDb();
        bool ok = await store.SoftDeleteAsync(platform, build, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    [HttpPost("delete")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest req, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return NoDb();
        int deleted = 0;
        foreach (var v in req.Versions ?? []) {
            if (await store.SoftDeleteAsync(v.Platform, v.Build, ct))
                deleted++;
        }

        return Ok(new { ok = true, deleted });
    }

    [HttpGet("merge-suggestions")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> MergeSuggestions(CancellationToken ct) {
        if (Store is not { } store) return Ok(Array.Empty<object>());
        var suggestions = await store.SuggestMergesAsync(ct);
        return Ok(suggestions);
    }

    [HttpPost("merge")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Merge([FromBody] MergeRequest req, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return NoDb();
        if (req?.Canonical is null || req.Aliases is null || req.Aliases.Count == 0)
            return Fail(400, "canonical + at least one alias required");
        var aliases = req.Aliases.Select(a => (a.Platform, a.Build)).ToList();
        int linked = await store.MergeAsync((req.Canonical.Platform, req.Canonical.Build), aliases, ct);
        return Ok(new { ok = true, linked });
    }

    [HttpPost("sha-order")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> SetShaOrder([FromBody] ShaOrderRequest req, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return NoDb();
        if (string.IsNullOrWhiteSpace(req.ProtoSha)) return Fail(400, "protoSha required");
        await store.SetShaOrderAsync(req.ProtoSha.Trim(), req.Order, user.Username, ct);
        return Ok(new { ok = true, protoSha = req.ProtoSha, order = req.Order });
    }

    [HttpGet("deleted")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Deleted(CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return Ok(Array.Empty<DeletedVersionRow>());
        var rows = await store.DeletedAsync(ct);
        return Ok(rows.Select(r => new DeletedVersionRow(
            r.Platform, r.Build, r.AppVersion, r.ClientVersion, r.Source, r.ProtoSha, r.DeletedAt)));
    }

    [HttpPost("{platform}/{build}/restore")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Restore(string platform, string build, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (Store is not { } store) return NoDb();
        bool ok = await store.RestoreAsync(platform, build, ct);
        return ok ? Ok(new { ok = true }) : NotFound();
    }

    [HttpGet("/api/protos/staged/check")]
    [ApiAccess(ApiAccessLevel.Public)]
    public async Task<IActionResult> StagedCheck([FromQuery] string protoSha, [FromQuery] string? platform,
        [FromQuery] string? appVersion, [FromQuery] string? build, [FromQuery] string? clientVersion,
        CancellationToken ct) {
        if (StagedStore is not { } s) return Ok(default(StagedProtoStore.CheckOutcome));
        return Ok(await s.CheckAsync(platform, appVersion, build, clientVersion, protoSha, ct));
    }

    [HttpPost("/api/protos/staged/correction")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> StagedCorrection([FromBody] CorrectionRequest req, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        if (string.IsNullOrWhiteSpace(req.TargetPlatform) || string.IsNullOrWhiteSpace(req.TargetBuild))
            return Fail(400, "targetPlatform + targetBuild required");
        string? by = user.DiscordId;
        var r = await s.StageCorrectionAsync(req.TargetPlatform, req.TargetBuild, req.Platform, req.AppVersion,
            req.Build, req.ClientVersion, req.Package, req.ProtoSha, req.ProtoText, req.MessageIndex, by, ct);
        return Ok(new { result = r.ToString().ToLowerInvariant() });
    }

    [HttpPost("/api/protos/staged/offer")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> StagedOffer([FromBody] OfferRequest req, CancellationToken ct) {
        if (StagedStore is not { } s) return NoDb();
        if (string.IsNullOrEmpty(req.ProtoSha) || string.IsNullOrEmpty(req.ProtoText))
            return Fail(400, "protoSha + protoText required");
        string? by = user.DiscordId;
        var r = await s.OfferAsync(req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package,
            req.ProtoSha, req.ProtoText, req.MessageIndex, by, "offer", ct);
        return Ok(new { result = r.ToString().ToLowerInvariant() });
    }

    [HttpGet("/api/protos/staged/count")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> StagedCount(CancellationToken ct) {
        if (StagedStore is not { } s) return Ok(new { count = 0 });
        return Ok(new { count = await s.PendingCountAsync(ct) });
    }

    [HttpGet("/api/protos/staged")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedList(CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return Ok(Array.Empty<StagedProtoStore.PendingRow>());
        return Ok(await s.PendingWithTargetsAsync(ct));
    }

    [HttpGet("/api/protos/staged/{id:int}/proto")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedProto(int id, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        var row = await s.PendingByIdAsync(id, ct);
        if (row is null || string.IsNullOrEmpty(row.ProtoText)) return NotFound();
        return Content(row.ProtoText, "text/plain");
    }

    [HttpPost("/api/protos/staged/{id:int}/approve")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedApprove(int id, [FromBody] ApproveRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        string who = user.DiscordId ?? "?";
        string? kind = await s.KindOfPendingAsync(id, ct);
        if (kind is null) return NotFound();
        if (kind == StagedProtoStore.KindCorrection && Require(UserRole.Admin) is { } noAdmin) return noAdmin;
        var r = await s.ApproveAsync(id, req.Platform, req.AppVersion, req.Build, req.ClientVersion, who, ct);
        return r switch {
            StagedProtoStore.ApproveResult.Ok => Ok(new { ok = true, merged = false }),
            StagedProtoStore.ApproveResult.Merged => Ok(new { ok = true, merged = true }),
            StagedProtoStore.ApproveResult.MissingBuild => Fail(400, "appVersion + build required to approve"),
            StagedProtoStore.ApproveResult.BuildCollision => Fail(409, "build already taken"),
            _ => NotFound()
        };
    }

    [HttpPost("/api/protos/staged/{id:int}/reject")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedReject(int id, [FromBody] RejectRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        string who = user.DiscordId ?? "?";
        return await s.RejectAsync(id, req.Note, who, ct) ? Ok(new { ok = true }) : NotFound();
    }

    [HttpPost("/api/protos/staged/bulk-approve")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedBulkApprove([FromBody] BulkApproveRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        string who = user.DiscordId ?? "?";
        var items = (req.Items ?? [])
            .Select(i => new StagedProtoStore.ApproveItem(i.Id, i.Platform, i.AppVersion, i.Build, i.ClientVersion))
            .ToList();
        var r = await s.BulkApproveAsync(items, who, user.IsAtLeast(UserRole.Admin), ct);
        return Ok(new { ok = true, approved = r.Approved, skipped = r.Skipped, failed = r.Failed });
    }

    [HttpPost("/api/protos/staged/bulk-reject")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedBulkReject([FromBody] BulkRejectRequest req, CancellationToken ct) {
        if (Require(UserRole.Contributor) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        string who = user.DiscordId ?? "?";
        int rejected = await s.BulkRejectAsync(req.Ids ?? [], req.Note, who, ct);
        return Ok(new { ok = true, rejected });
    }

    [HttpPost("/api/protos/staged/import-crawl")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> ImportCrawl(IFormFile file, CancellationToken ct) {
        if (Require(UserRole.Admin) is { } no) return no;
        if (StagedStore is not { } s) return NoDb();
        if (file is null || file.Length == 0) return Fail(400, "zip file required");
        byte[] bytes;
        using (var ms = new MemoryStream()) {
            await file.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        IReadOnlyList<CrawlManifestReader.CrawlRecord> records;
        try {
            records = CrawlManifestReader.Read(bytes);
        } catch (Exception ex) {
            return Fail(400, $"bad dataset zip: {ex.Message}");
        }

        (int staged, int skipped) = await s.ImportCrawlAsync(records, ct);
        return Ok(new { staged, skipped });
    }
}
