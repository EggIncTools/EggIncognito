using System.Text.Json;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Core.Services.Protos;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class StagedProtoStore(EggIncognitoDbContext db, TimeProvider time, ProtoRegistryStore registry) {
    public const string KindVersion = "version";
    public const string KindCorrection = "correction";
    public const string SourceOffer = "offer";

    public enum ApproveResult {
        Ok,
        Merged,
        NotFound,
        MissingBuild,
        BuildCollision,
        Forbidden
    }

    public enum OfferResult {
        Staged,
        AlreadyPending,
        AlreadyInRegistry,
        CorrectionStaged,
        ArchiveFlagged,
        AlreadyArchived
    }

    public enum CorrectionResult {
        Staged,
        AlreadyPending,
        NoChange,
        NotFound
    }

    private Task NotifyStagedAsync(string key, CancellationToken ct) =>
        PgNotify.SendAsync(db, PgChannels.StagedProtos, key, ct);

    private async Task<bool> ShaPendingAsync(string sha, string? platform, CancellationToken ct) {
        string? p = string.IsNullOrWhiteSpace(platform) ? null : platform.Trim();
        return await db.StagedProtos.AnyAsync(
            s => s.ProtoSha == sha && s.Status == "pending" && (p == null || EF.Functions.ILike(s.Platform, p)), ct);
    }

    public static List<ProtoVersion> OnPlatform(IReadOnlyList<ProtoVersion> rows, string? platform) =>
        string.IsNullOrWhiteSpace(platform) ? [.. rows] : [.. rows.Where(p => Same(p.Platform, platform))];

    public static CheckOutcome Evaluate(IReadOnlyList<ProtoVersion> shaRows, bool pending, string? platform,
        string? appVersion, string? build, string? clientVersion) {
        var rows = OnPlatform(shaRows, platform);
        bool inReg = rows.Count > 0;
        var compatible = rows.Where(p => AllCompatible(p, platform, appVersion, build, clientVersion)).ToList();
        bool known = compatible.Count > 0;
        bool archived = known && compatible.TrueForAll(p => p.ArchiveSourced);
        bool conflict = inReg && !known;
        var stored = conflict
            ? new StoredMeta(rows[0].Platform, rows[0].AppVersion, rows[0].Build, rows[0].ClientVersion)
            : (StoredMeta?)null;
        return new CheckOutcome(inReg, pending, known, conflict, stored, archived);
    }

    private static int FieldScore(string? appVersion, string? build, string? clientVersion) =>
        (string.IsNullOrWhiteSpace(appVersion) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(build) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(clientVersion) ? 0 : 1);

    private static bool AllCompatible(ProtoVersion p, string? platform, string? appVersion, string? build,
        string? clientVersion) =>
        FieldCompatible(p.Platform, platform)
        && FieldCompatible(p.AppVersion, appVersion)
        && FieldCompatible(p.Build, build)
        && FieldCompatible(p.ClientVersion, clientVersion);

    private Task<List<ProtoVersion>> ShaRowsAsync(string sha, CancellationToken ct) =>
        db.ProtoVersions.AsNoTracking().Where(p => p.ProtoSha == sha && p.DeletedAt == null).ToListAsync(ct);

    private async Task<bool> CorrectionPendingAsync(int targetId, string? appVersion, string? build,
        string? clientVersion, CancellationToken ct) {
        var pending = await db.StagedProtos.AsNoTracking()
            .Where(s => s.Status == "pending" && s.Kind == KindCorrection && s.TargetId == targetId)
            .Select(s => new { s.AppVersion, s.Build, s.ClientVersion })
            .ToListAsync(ct);
        return pending.Any(p => Same(p.AppVersion, appVersion) && Same(p.Build, build)
                                                              && Same(p.ClientVersion, clientVersion));
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);

    private async Task<StageOutcome> StageOrReviveAsync(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string source, string? submittedBy,
        string? originRepo, string? originCommit, DateTimeOffset? originDate, string? confidence,
        CancellationToken ct) {
        var shaRows = OnPlatform(await ShaRowsAsync(protoSha, ct), platform);
        if (shaRows.Count > 0) {
            if (shaRows.Any(p => AllCompatible(p, platform, appVersion, build, clientVersion)))
                return StageOutcome.AlreadyInRegistry;

            var target = shaRows[0];
            if (await CorrectionPendingAsync(target.Id, appVersion, build, clientVersion, ct))
                return StageOutcome.AlreadyPending;

            var correction = NewStaged(platform, appVersion, build, clientVersion, package, protoSha, protoText,
                messageIndex, source, submittedBy, time.GetUtcNow());
            correction.Kind = KindCorrection;
            correction.TargetId = target.Id;
            correction.OriginRepo = originRepo;
            correction.OriginCommit = originCommit;
            correction.OriginDate = originDate;
            correction.Confidence = confidence;
            db.StagedProtos.Add(correction);
            await db.SaveChangesAsync(ct);
            await NotifyStagedAsync($"stage:{correction.Id}", ct);
            return StageOutcome.CorrectionStaged;
        }

        if (await ShaPendingAsync(protoSha, platform, ct)) return StageOutcome.AlreadyPending;

        var now = time.GetUtcNow();
        int incomingScore = FieldScore(appVersion, build, clientVersion);

        var rejected = await db.StagedProtos
            .Where(s => s.ProtoSha == protoSha && s.Status == "rejected")
            .OrderByDescending(s => s.ReviewedAt).FirstOrDefaultAsync(ct);
        if (rejected is not null) {
            if (incomingScore <= FieldScore(rejected.AppVersion, rejected.Build, rejected.ClientVersion))
                return StageOutcome.StaleRejected;
            rejected.AppVersion = string.IsNullOrWhiteSpace(rejected.AppVersion) ? appVersion : rejected.AppVersion;
            rejected.Build = string.IsNullOrWhiteSpace(rejected.Build) ? build : rejected.Build;
            rejected.ClientVersion = string.IsNullOrWhiteSpace(rejected.ClientVersion)
                ? clientVersion
                : rejected.ClientVersion;
            rejected.Platform = string.IsNullOrWhiteSpace(rejected.Platform) ? platform : rejected.Platform;
            rejected.Confidence ??= confidence;
            rejected.OriginRepo ??= originRepo;
            rejected.OriginCommit ??= originCommit;
            rejected.OriginDate ??= originDate;
            rejected.Status = "pending";
            rejected.ReviewedBy = null;
            rejected.ReviewedAt = null;
            rejected.ReviewNote = null;
            rejected.SubmittedAt = now;
            await db.SaveChangesAsync(ct);
            await NotifyStagedAsync($"revive:{rejected.Id}", ct);
            return StageOutcome.Revived;
        }

        var fresh = NewStaged(platform, appVersion, build, clientVersion, package, protoSha, protoText,
            messageIndex, source, submittedBy, now);
        fresh.OriginRepo = originRepo;
        fresh.OriginCommit = originCommit;
        fresh.OriginDate = originDate;
        fresh.Confidence = confidence;
        db.StagedProtos.Add(fresh);
        await db.SaveChangesAsync(ct);
        await NotifyStagedAsync($"stage:{fresh.Id}", ct);
        return StageOutcome.Staged;
    }

    private static StagedProto NewStaged(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string source, string? submittedBy,
        DateTimeOffset now) =>
        new() {
            Platform = platform,
            AppVersion = appVersion,
            Build = build,
            ClientVersion = clientVersion,
            Package = package,
            ProtoSha = protoSha,
            ProtoText = protoText,
            MessageIndex = messageIndex,
            Source = source,
            Status = "pending",
            SubmittedBy = submittedBy,
            SubmittedAt = now
        };

    public async Task<CorrectionResult> StageCorrectionAsync(
        string targetPlatform, string targetBuild, string platform, string? appVersion, string? build,
        string? clientVersion, string? package, string protoSha, string protoText, string? messageIndex,
        string? submittedBy, CancellationToken ct) {
        var target = await db.ProtoVersions.AsNoTracking().FirstOrDefaultAsync(
            p => p.Platform == targetPlatform && p.Build == targetBuild && p.DeletedAt == null, ct);
        if (target is null) return CorrectionResult.NotFound;

        bool metaSame = Unchanged(appVersion, target.AppVersion)
                        && Unchanged(build, target.Build)
                        && Unchanged(clientVersion, target.ClientVersion);
        bool shaSame = Same(protoSha, target.ProtoSha);
        if (metaSame && shaSame) return CorrectionResult.NoChange;

        if (await CorrectionPendingAsync(target.Id, appVersion, build, clientVersion, ct))
            return CorrectionResult.AlreadyPending;

        var row = NewStaged(platform, appVersion, build, clientVersion, package, protoSha, protoText,
            messageIndex, "overwrite", submittedBy, time.GetUtcNow());
        row.Kind = KindCorrection;
        row.TargetId = target.Id;
        db.StagedProtos.Add(row);
        await db.SaveChangesAsync(ct);
        await NotifyStagedAsync($"stage:{row.Id}", ct);
        return CorrectionResult.Staged;
    }

    private static bool Unchanged(string? proposed, string? stored) =>
        string.IsNullOrWhiteSpace(proposed) || Same(proposed, stored);

    private async Task<OfferResult?> FlagArchiveMatchAsync(string platform, string? appVersion, string? build,
        string? clientVersion, string protoSha, CancellationToken ct) {
        var matches = OnPlatform(await ShaRowsAsync(protoSha, ct), platform)
            .Where(p => AllCompatible(p, platform, appVersion, build, clientVersion))
            .ToList();
        if (matches.Count == 0) return null;
        var unflagged = matches.Where(p => !p.ArchiveSourced).ToList();
        if (unflagged.Count == 0) return OfferResult.AlreadyArchived;
        foreach (var p in unflagged) await registry.SetArchiveSourcedAsync(p.Platform, p.Build, ct);
        return OfferResult.ArchiveFlagged;
    }

    public async Task<OfferResult> OfferAsync(
        string platform, string? appVersion, string? build, string? clientVersion, string? package,
        string protoSha, string protoText, string? messageIndex, string? submittedBy, string source,
        bool canFlagArchive, CancellationToken ct) {
        if (canFlagArchive && source == SourceOffer
            && await FlagArchiveMatchAsync(platform, appVersion, build, clientVersion, protoSha, ct) is { } flagged)
            return flagged;
        var outcome = await StageOrReviveAsync(platform, appVersion, build, clientVersion, package, protoSha,
            protoText, messageIndex, source, submittedBy, null, null, null, null, ct);
        return outcome switch {
            StageOutcome.AlreadyInRegistry => OfferResult.AlreadyInRegistry,
            StageOutcome.CorrectionStaged => OfferResult.CorrectionStaged,
            StageOutcome.AlreadyPending or StageOutcome.StaleRejected => OfferResult.AlreadyPending,
            _ => OfferResult.Staged
        };
    }

    public async Task<(int staged, int skipped)> ImportCrawlAsync(
        IReadOnlyList<CrawlManifestReader.CrawlRecord> records, CancellationToken ct) {
        int staged = 0, skipped = 0;
        foreach (var r in records) {
            (string sha, string? messageIndex) = ProtoCanonicalForm.Normalize(r.ProtoText) is { Ok: true, Sha: { } canonSha, Text: { } canonText }
                ? (canonSha, JsonSerializer.Serialize(ProtoTextIndex.Names(canonText)))
                : (r.ProtoSha, null);
            var outcome = await StageOrReviveAsync(r.Platform, r.AppVersion, r.Build, r.ClientVersion, null,
                sha, r.ProtoText, messageIndex, "crawl", null, r.OriginRepo, r.OriginCommit, r.OriginDate,
                r.Confidence, ct);
            if (outcome is StageOutcome.Staged or StageOutcome.Revived or StageOutcome.CorrectionStaged) staged++;
            else skipped++;
        }

        return (staged, skipped);
    }

    public Task<List<StagedProto>> PendingAsync(CancellationToken ct) =>
        db.StagedProtos.AsNoTracking().Where(s => s.Status == "pending")
            .OrderByDescending(s => s.SubmittedAt).ToListAsync(ct);

    public Task<StagedProto?> PendingByIdAsync(int id, CancellationToken ct) =>
        db.StagedProtos.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);

    public Task<int> PendingCountAsync(CancellationToken ct) =>
        db.StagedProtos.CountAsync(s => s.Status == "pending", ct);

    public static bool FieldCompatible(string? a, string? b) {
        string left = a?.Trim() ?? "";
        string right = b?.Trim() ?? "";
        return left.Length == 0 || right.Length == 0
                                || string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<CheckOutcome> CheckAsync(
        string? platform, string? appVersion, string? build, string? clientVersion, string protoSha,
        CancellationToken ct) {
        var rows = await ShaRowsAsync(protoSha, ct);
        bool pending = await ShaPendingAsync(protoSha, platform, ct);
        return Evaluate(rows, pending, platform, appVersion, build, clientVersion);
    }

    public Task<List<PendingRow>> PendingWithTargetsAsync(CancellationToken ct) =>
        db.StagedProtos.AsNoTracking()
            .Where(s => s.Status == "pending")
            .LeftJoin(db.ProtoVersions.AsNoTracking(), s => s.TargetId, p => (int?)p.Id, (s, t) => new { s, t })
            .OrderByDescending(x => x.s.SubmittedAt)
            .Select(x => new PendingRow(
                x.s.Id, x.s.Source, x.s.Platform, x.s.AppVersion, x.s.Build, x.s.ClientVersion, x.s.ProtoSha,
                x.s.SubmittedBy, x.s.SubmittedAt, x.s.OriginRepo, x.s.OriginCommit, x.s.OriginDate, x.s.Confidence,
                x.s.Kind, x.s.TargetId,
                x.t == null
                    ? null
                    : new TargetMeta(x.t.Platform, x.t.AppVersion, x.t.Build, x.t.ClientVersion, x.t.ProtoSha)))
            .ToListAsync(ct);

    public async Task<ApproveResult> ApproveAsync(
        int id, string? platform, string? appVersion, string? build, string? clientVersion,
        string reviewedBy, bool allowCorrection, CancellationToken ct) {
        var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);
        if (row is null) return ApproveResult.NotFound;
        if (row.Kind == KindCorrection && !allowCorrection) return ApproveResult.Forbidden;

        string plat = string.IsNullOrWhiteSpace(platform) ? row.Platform : platform;
        string? appV = string.IsNullOrWhiteSpace(appVersion) ? row.AppVersion : appVersion;
        string? bld = string.IsNullOrWhiteSpace(build) ? row.Build : build;
        string? cv = string.IsNullOrWhiteSpace(clientVersion) ? row.ClientVersion : clientVersion;

        if (row.Kind == KindCorrection) {
            var target = row.TargetId is { } tid
                ? await db.ProtoVersions.FirstOrDefaultAsync(p => p.Id == tid, ct)
                : null;
            if (target is null) return ApproveResult.NotFound;

            string targetPlatform = target.Platform;
            string targetBuild = target.Build;
            string? newAppV = string.IsNullOrWhiteSpace(appV) ? null : appV;
            string? newCv = string.IsNullOrWhiteSpace(cv) ? null : cv;
            var meta = await registry.UpdateMetadataAsync(targetPlatform, targetBuild, newAppV, newCv, null, bld, ct);
            if (meta == ProtoRegistryStore.MetadataUpdate.BuildCollision) return ApproveResult.BuildCollision;
            if (meta == ProtoRegistryStore.MetadataUpdate.NotFound) return ApproveResult.NotFound;

            string finalBuild = string.IsNullOrWhiteSpace(bld) ? targetBuild : bld;
            if (!string.IsNullOrEmpty(row.ProtoText) && row.ProtoSha != target.ProtoSha)
                await registry.SetProtoAsync(targetPlatform, finalBuild, row.ProtoText, ct);
            if (row.Source == SourceOffer) await registry.SetArchiveSourcedAsync(targetPlatform, finalBuild, ct);

            row.Status = "approved";
            row.ReviewedBy = reviewedBy;
            row.ReviewedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await NotifyStagedAsync($"approve:{row.Id}", ct);
            return ApproveResult.Merged;
        }

        if (string.IsNullOrWhiteSpace(bld) || string.IsNullOrWhiteSpace(appV)) return ApproveResult.MissingBuild;

        var existing = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == plat && p.Build == bld, ct);
        var result = existing is null ? ApproveResult.Ok : ApproveResult.Merged;

        if (existing is null) {
            await registry.UpsertAsync(plat, appV, bld, cv, row.Package ?? "",
                row.ProtoSha, $"staged:{row.Id}", time.GetUtcNow(),
                $"staged-approve:{reviewedBy}", row.ProtoText, row.Source,
                true, ct);
        } else {
            bool hasProto = await db.ProtoProtos.AnyAsync(x => x.ProtoVersionId == existing.Id, ct);
            await registry.BackfillUpsertAsync(plat, appV, bld, cv, row.Package ?? "",
                row.ProtoText, row.ProtoSha, row.MessageIndex,
                !hasProto, $"staged:{row.Id}", time.GetUtcNow(),
                row.Source, ct);
        }

        if (row.Source == SourceOffer) await registry.SetArchiveSourcedAsync(plat, bld, ct);

        row.Status = "approved";
        row.ReviewedBy = reviewedBy;
        row.ReviewedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyStagedAsync($"approve:{row.Id}", ct);
        return result;
    }

    public async Task<bool> RejectAsync(int id, string? note, string reviewedBy, CancellationToken ct) {
        var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id && s.Status == "pending", ct);
        if (row is null) return false;
        row.Status = "rejected";
        row.ReviewNote = note;
        row.ReviewedBy = reviewedBy;
        row.ReviewedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyStagedAsync($"reject:{row.Id}", ct);
        return true;
    }

    public async Task<BulkApproveResult> BulkApproveAsync(
        IReadOnlyList<ApproveItem> items, string reviewedBy, bool allowCorrections, CancellationToken ct) {
        int ok = 0, skipped = 0, failed = 0;
        foreach (var it in items) {
            var r = await ApproveAsync(it.Id, it.Platform, it.AppVersion, it.Build, it.ClientVersion, reviewedBy,
                allowCorrections, ct);
            switch (r) {
                case ApproveResult.Ok or ApproveResult.Merged: ok++; break;
                case ApproveResult.MissingBuild or ApproveResult.Forbidden: skipped++; break;
                default: failed++; break;
            }
        }

        return new BulkApproveResult(ok, skipped, failed);
    }

    public async Task<int> BulkRejectAsync(IReadOnlyList<int> ids, string? note, string reviewedBy,
        CancellationToken ct) {
        var now = time.GetUtcNow();
        var rows = await db.StagedProtos.Where(s => ids.Contains(s.Id) && s.Status == "pending").ToListAsync(ct);
        foreach (var row in rows) {
            row.Status = "rejected";
            row.ReviewNote = note;
            row.ReviewedBy = reviewedBy;
            row.ReviewedAt = now;
        }

        if (rows.Count > 0) {
            await db.SaveChangesAsync(ct);
            await NotifyStagedAsync($"bulk-reject:{rows.Count}", ct);
        }

        return rows.Count;
    }

    private enum StageOutcome {
        Staged,
        Revived,
        AlreadyPending,
        AlreadyInRegistry,
        StaleRejected,
        CorrectionStaged
    }

    public readonly record struct CheckOutcome(
        bool InRegistry,
        bool Pending,
        bool KnownCombination,
        bool Conflict,
        StoredMeta? Stored,
        bool Archived = false);

    public readonly record struct StoredMeta(
        string Platform,
        string? AppVersion,
        string? Build,
        string? ClientVersion);

    public sealed record TargetMeta(
        string Platform,
        string? AppVersion,
        string? Build,
        string? ClientVersion,
        string? ProtoSha);

    public sealed record PendingRow(
        int Id,
        string Source,
        string Platform,
        string? AppVersion,
        string? Build,
        string? ClientVersion,
        string ProtoSha,
        string? SubmittedBy,
        DateTimeOffset SubmittedAt,
        string? OriginRepo,
        string? OriginCommit,
        DateTimeOffset? OriginDate,
        string? Confidence,
        string Kind,
        int? TargetId,
        TargetMeta? Target);

    public readonly record struct ApproveItem(
        int Id,
        string? Platform,
        string? AppVersion,
        string? Build,
        string? ClientVersion);

    public readonly record struct BulkApproveResult(int Approved, int Skipped, int Failed);
}
