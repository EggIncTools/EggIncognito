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
public sealed class ProtoRegistryController(ICurrentUser user, TimeProvider time) : ApiControllerBase {
    private string Reviewer => user.DiscordId ?? "?";

    [HttpPost]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> Save([FromBody] SaveRequest req, [FromServices] ProtoRegistryStore store,
        CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(req.Platform) || string.IsNullOrWhiteSpace(req.Build) ||
            string.IsNullOrWhiteSpace(req.AppVersion))
            return Fail(400, "platform, appVersion, build required");

        string? protoText = string.IsNullOrWhiteSpace(req.Proto) ? null : req.Proto;
        string sha = protoText switch {
            null => "",
            _ when ProtoCanonicalForm.Normalize(protoText) is { Ok: true, Sha: { } canon } => canon,
            _ => ProtoHash.Of(protoText)
        };

        var upsert = await store.UpsertAsync(
            req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package ?? "",
            sha, "", time.GetUtcNow(), user.Username, protoText,
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
    [RequiresDb]
    public async Task<IActionResult> Edit(string platform, string build, [FromBody] EditRequest req,
        [FromServices] ProtoRegistryStore store, CancellationToken ct) {
        var result = await store.UpdateMetadataAsync(
            platform, build, req.AppVersion, req.ClientVersion, req.Source, req.Build, ct);
        return result switch {
            ProtoRegistryStore.MetadataUpdate.Ok => Ok(new { ok = true }),
            ProtoRegistryStore.MetadataUpdate.BuildCollision =>
                Fail(409, $"build '{req.Build}' already exists for {platform}"),
            _ => Fail(404, "version not found")
        };
    }

    [HttpPost("{platform}/{build}/proto")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> SetProto(string platform, string build, [FromBody] SetProtoRequest req,
        [FromServices] ProtoRegistryStore store, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(req.Proto)) return Fail(400, "proto required");
        return await store.SetProtoAsync(platform, build, req.Proto, ct) is { } sha
            ? Ok(new { ok = true, protoSha = sha })
            : Fail(404, "version not found");
    }

    [HttpDelete("{platform}/{build}")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> Delete(string platform, string build, [FromServices] ProtoRegistryStore store,
        CancellationToken ct) =>
        await store.SoftDeleteAsync(platform, build, ct) ? Ok(new { ok = true }) : Fail(404, "version not found");

    [HttpPost("delete")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest req,
        [FromServices] ProtoRegistryStore store, CancellationToken ct) {
        int deleted = 0;
        foreach (var v in req.Versions ?? []) {
            if (await store.SoftDeleteAsync(v.Platform, v.Build, ct))
                deleted++;
        }

        return Ok(new { ok = true, deleted });
    }

    [HttpGet("merge-suggestions")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> MergeSuggestions([FromServices] ProtoRegistryStore? store, CancellationToken ct) =>
        store is null ? Ok(Array.Empty<object>()) : Ok(await store.SuggestMergesAsync(ct));

    [HttpPost("merge")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> Merge([FromBody] MergeRequest? req, [FromServices] ProtoRegistryStore store,
        CancellationToken ct) {
        if (req is not { Canonical: { } canonical, Aliases: [_, ..] aliases })
            return Fail(400, "canonical + at least one alias required");
        int linked = await store.MergeAsync((canonical.Platform, canonical.Build),
            [.. aliases.Select(a => (a.Platform, a.Build))], ct);
        return Ok(new { ok = true, linked });
    }

    [HttpPost("sha-order")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> SetShaOrder([FromBody] ShaOrderRequest req,
        [FromServices] ProtoRegistryStore store, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(req.ProtoSha)) return Fail(400, "protoSha required");
        await store.SetShaOrderAsync(req.ProtoSha.Trim(), req.Order, user.Username, ct);
        return Ok(new { ok = true, protoSha = req.ProtoSha, order = req.Order });
    }

    [HttpGet("deleted")]
    [ApiAccess(ApiAccessLevel.Admin)]
    public async Task<IActionResult> Deleted([FromServices] ProtoRegistryStore? store, CancellationToken ct) {
        if (store is null) return Ok(Array.Empty<DeletedVersionRow>());
        var rows = await store.DeletedAsync(ct);
        return Ok(rows.Select(r => new DeletedVersionRow(
            r.Platform, r.Build, r.AppVersion, r.ClientVersion, r.Source, r.ProtoSha, r.DeletedAt)));
    }

    [HttpPost("{platform}/{build}/restore")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> Restore(string platform, string build, [FromServices] ProtoRegistryStore store,
        CancellationToken ct) =>
        await store.RestoreAsync(platform, build, ct) ? Ok(new { ok = true }) : Fail(404, "version not found");

    [HttpGet("/api/protos/staged/check")]
    [ApiAccess(ApiAccessLevel.Public)]
    public async Task<IActionResult> StagedCheck([FromQuery] string protoSha, [FromQuery] string? platform,
        [FromQuery] string? appVersion, [FromQuery] string? build, [FromQuery] string? clientVersion,
        [FromServices] StagedProtoStore? s, CancellationToken ct) =>
        Ok(s is null ? default : await s.CheckAsync(platform, appVersion, build, clientVersion, protoSha, ct));

    [HttpPost("/api/protos/staged/correction")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> StagedCorrection([FromBody] CorrectionRequest req,
        [FromServices] StagedProtoStore s, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(req.TargetPlatform) || string.IsNullOrWhiteSpace(req.TargetBuild))
            return Fail(400, "targetPlatform + targetBuild required");
        var r = await s.StageCorrectionAsync(req.TargetPlatform, req.TargetBuild, req.Platform, req.AppVersion,
            req.Build, req.ClientVersion, req.Package, req.ProtoSha, req.ProtoText, req.MessageIndex, user.DiscordId,
            ct);
        return Ok(new { result = r.ToString().ToLowerInvariant() });
    }

    [HttpPost("/api/protos/staged/offer")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    [RequiresDb]
    public async Task<IActionResult> StagedOffer([FromBody] OfferRequest req, [FromServices] StagedProtoStore s,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(req.ProtoSha) || string.IsNullOrEmpty(req.ProtoText))
            return Fail(400, "protoSha + protoText required");
        var r = await s.OfferAsync(req.Platform, req.AppVersion, req.Build, req.ClientVersion, req.Package,
            req.ProtoSha, req.ProtoText, req.MessageIndex, user.DiscordId, "offer", ct);
        return Ok(new { result = r.ToString().ToLowerInvariant() });
    }

    [HttpGet("/api/protos/staged/count")]
    [ApiAccess(ApiAccessLevel.Authenticated)]
    public async Task<IActionResult> StagedCount([FromServices] StagedProtoStore? s, CancellationToken ct) =>
        Ok(new { count = s is null ? 0 : await s.PendingCountAsync(ct) });

    [HttpGet("/api/protos/staged")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    public async Task<IActionResult> StagedList([FromServices] StagedProtoStore? s, CancellationToken ct) =>
        Ok(s is null ? [] : await s.PendingWithTargetsAsync(ct));

    [HttpGet("/api/protos/staged/{id:int}/proto")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> StagedProto(int id, [FromServices] StagedProtoStore s, CancellationToken ct) =>
        await s.PendingByIdAsync(id, ct) is { ProtoText: { Length: > 0 } text }
            ? Content(text, "text/plain")
            : Fail(404, "staged proto not found");

    [HttpPost("/api/protos/staged/{id:int}/approve")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> StagedApprove(int id, [FromBody] ApproveRequest req,
        [FromServices] StagedProtoStore s, CancellationToken ct) {
        var r = await s.ApproveAsync(id, req.Platform, req.AppVersion, req.Build, req.ClientVersion, Reviewer,
            user.IsAtLeast(UserRole.Admin), ct);
        return r switch {
            StagedProtoStore.ApproveResult.Ok => Ok(new { ok = true, merged = false }),
            StagedProtoStore.ApproveResult.Merged => Ok(new { ok = true, merged = true }),
            StagedProtoStore.ApproveResult.MissingBuild => Fail(400, "appVersion + build required to approve"),
            StagedProtoStore.ApproveResult.BuildCollision => Fail(409, "build already taken"),
            StagedProtoStore.ApproveResult.Forbidden => Fail(403, "admin+ only"),
            _ => Fail(404, "staged proto not found")
        };
    }

    [HttpPost("/api/protos/staged/{id:int}/reject")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> StagedReject(int id, [FromBody] RejectRequest req,
        [FromServices] StagedProtoStore s, CancellationToken ct) =>
        await s.RejectAsync(id, req.Note, Reviewer, ct) ? Ok(new { ok = true }) : Fail(404, "staged proto not found");

    [HttpPost("/api/protos/staged/bulk-approve")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> StagedBulkApprove([FromBody] BulkApproveRequest req,
        [FromServices] StagedProtoStore s, CancellationToken ct) {
        var r = await s.BulkApproveAsync(
            [.. (req.Items ?? []).Select(i =>
                new StagedProtoStore.ApproveItem(i.Id, i.Platform, i.AppVersion, i.Build, i.ClientVersion))],
            Reviewer, user.IsAtLeast(UserRole.Admin), ct);
        return Ok(new { ok = true, approved = r.Approved, skipped = r.Skipped, failed = r.Failed });
    }

    [HttpPost("/api/protos/staged/bulk-reject")]
    [ApiAccess(ApiAccessLevel.Contributor)]
    [RequiresDb]
    public async Task<IActionResult> StagedBulkReject([FromBody] BulkRejectRequest req,
        [FromServices] StagedProtoStore s, CancellationToken ct) =>
        Ok(new { ok = true, rejected = await s.BulkRejectAsync(req.Ids ?? [], req.Note, Reviewer, ct) });

    [HttpPost("/api/protos/staged/import-crawl")]
    [ApiAccess(ApiAccessLevel.Admin)]
    [RequiresDb]
    public async Task<IActionResult> ImportCrawl(IFormFile? file, [FromServices] StagedProtoStore s,
        CancellationToken ct) {
        if (file is not { Length: > 0 }) return Fail(400, "zip file required");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        IReadOnlyList<CrawlManifestReader.CrawlRecord> records;
        try {
            records = CrawlManifestReader.Read(ms.ToArray());
        } catch (Exception ex) {
            return Fail(400, $"bad dataset zip: {ex.Message}");
        }

        (int staged, int skipped) = await s.ImportCrawlAsync(records, ct);
        return Ok(new { staged, skipped });
    }
}
