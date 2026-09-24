using System.Text.Json;
using EggIncognito.Core;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public interface IProtoBackfillStore {
    Task<ProtoVersion?> GetAsync(string platform, string build, CancellationToken ct = default);

    Task BackfillUpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string? protoText, string? protoSha, string? messageIndex, bool writeProto,
        string apkRef, DateTimeOffset detectedAt, string source, CancellationToken ct = default);

    Task<int> PruneEmptyAsync(CancellationToken ct = default);

    Task<List<(string Platform, string Build, string ProtoText)>> LatestProtoTextsAsync(CancellationToken ct = default);
}

public sealed class ProtoRegistryStore(EggIncognitoDbContext db, TimeProvider time, IEnumerable<IProtoUpsertObserver> observers)
    : IProtoBackfillStore {
    public enum MetadataUpdate {
        Ok,
        NotFound,
        BuildCollision
    }

    public async Task BackfillUpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string? protoText, string? protoSha, string? messageIndex, bool writeProto,
        string apkRef, DateTimeOffset detectedAt, string source, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(build)) return;

        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) {
            row = new ProtoVersion { Platform = platform, Build = build, Source = source };
            db.ProtoVersions.Add(row);
        }

        row.Package = package;
        if (string.IsNullOrEmpty(row.AppVersion) || source == "farm") row.AppVersion = appVersion;
        row.ClientVersion ??= clientVersion;
        if (string.IsNullOrEmpty(row.ApkRef)) row.ApkRef = apkRef;
        if (row.DetectedAt == default) row.DetectedAt = detectedAt;
        await db.SaveChangesAsync(ct);

        if (writeProto && !string.IsNullOrEmpty(protoText)) {
            row.ProtoSha = protoSha ?? "";
            await UpsertProtoProtoAsync(row.Id, protoText, messageIndex ?? "[]", row.ProtoSha, ct);
        }

        await NotifyRegistryAsync($"backfill:{platform}:{build}", ct);
    }

    private Task NotifyRegistryAsync(string key, CancellationToken ct) =>
        PgNotify.SendAsync(db, PgChannels.ProtoRegistry, key, ct);

    private async Task UpsertProtoProtoAsync(int protoVersionId, string protoText, string? messageIndex,
        string protoSha, CancellationToken ct) {
        var pp = await db.ProtoProtos.FirstOrDefaultAsync(x => x.ProtoVersionId == protoVersionId, ct);
        if (pp is null) {
            pp = new ProtoProto { ProtoVersionId = protoVersionId };
            db.ProtoProtos.Add(pp);
        }

        pp.ProtoText = protoText;
        pp.MessageIndex = messageIndex ?? JsonSerializer.Serialize(ProtoTextIndex.Names(protoText));
        await db.SaveChangesAsync(ct);
        await EnsureCanonicalAsync(protoSha, protoText, ct);
    }

    private async Task EnsureCanonicalAsync(string sha, string rawText, CancellationToken ct) {
        if (string.IsNullOrEmpty(sha)) return;
        if (await db.ProtoCanonicals.AsNoTracking().AnyAsync(c => c.ProtoSha == sha, ct)) return;

        ProtoCanonicalForm.NormalizeResult norm;
        try {
            norm = ProtoCanonicalForm.Normalize(rawText);
        } catch (Exception ex) {
            norm = new ProtoCanonicalForm.NormalizeResult(false, null, null, ex.Message);
        }

        var entity = new ProtoCanonical {
            ProtoSha = sha,
            CanonicalText = norm.Ok ? norm.Text : null,
            CanonicalSha = norm.Ok ? norm.Sha : null,
            Ok = norm.Ok,
            Error = norm.Ok ? null : norm.Error
        };
        db.ProtoCanonicals.Add(entity);
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException) {
            db.Entry(entity).State = EntityState.Detached;
        }
    }

    public async Task<List<(string Platform, string Build, string ProtoText)>> LatestProtoTextsAsync(
        CancellationToken ct = default) {
        var rows = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.DeletedAt == null)
            .Where(p => p.Build != null && p.Build != "" && p.AppVersion != null && p.AppVersion != "")
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.Id, p.Platform, p.Build })
            .ToListAsync(ct);

        var result = new List<(string, string, string)>();
        foreach (var latest in rows.DistinctBy(r => r.Platform)) {
            var pp = await db.ProtoProtos.AsNoTracking()
                .FirstOrDefaultAsync(x => x.ProtoVersionId == latest.Id, ct);
            if (pp is not null && !string.IsNullOrEmpty(pp.ProtoText))
                result.Add((latest.Platform, latest.Build, pp.ProtoText));
        }

        return result;
    }

    public Task<int> PruneEmptyAsync(CancellationToken ct = default) =>
        db.ProtoVersions
            .Where(p => p.Build == null || p.Build == "" || p.AppVersion == null || p.AppVersion == "")
            .ExecuteDeleteAsync(ct);

    public Task<ProtoVersion?> GetAsync(string platform, string build, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);

    public async Task<UpsertOutcome> UpsertAsync(
        string platform, string appVersion, string build, string? clientVersion, string package,
        string protoSha, string apkRef, DateTimeOffset detectedAt, string? detectedBy, string? protoText,
        string source = "farm", bool resurrect = false, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(build) || string.IsNullOrEmpty(appVersion)) {
            return new UpsertOutcome(
                new ProtoVersion { Platform = platform, Build = build, AppVersion = appVersion },
                false, false, VersionDelta.Unknown, null, null);
        }

        var prevLatest = await PreviousBestAsync(platform, build, ct);

        var row = await db.ProtoVersions
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        bool created = row is null;
        if (row is null) {
            row = new ProtoVersion { Platform = platform, Build = build };
            db.ProtoVersions.Add(row);
        }

        row.AppVersion = appVersion;
        row.ClientVersion = clientVersion;
        row.Source = source;
        row.Package = package;
        row.ProtoSha = protoSha;
        row.ApkRef = apkRef;
        row.DetectedAt = detectedAt;
        row.DetectedBy = detectedBy;
        if (resurrect) {
            row.DeletedAt = null;
            row.CanonicalId = null;
        }

        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(protoText))
            await UpsertProtoProtoAsync(row.Id, protoText, null, row.ProtoSha, ct);

        bool protoChanged = prevLatest is not null && prevLatest.ProtoSha != protoSha;
        var delta = VersionDeltaCalc.Classify(
            platform, build, appVersion, prevLatest?.Build, prevLatest?.AppVersion);
        var outcome = new UpsertOutcome(row, created, protoChanged, delta,
            prevLatest?.AppVersion, prevLatest?.Build);

        await NotifyAsync(new ProtoUpsertNotice(
            row.Id, platform, appVersion, build, clientVersion, protoSha,
            !string.IsNullOrEmpty(protoText), created, protoChanged, delta,
            prevLatest?.AppVersion, prevLatest?.Build), ct);
        await NotifyRegistryAsync($"upsert:{platform}:{build}", ct);
        return outcome;
    }

    private async Task NotifyAsync(ProtoUpsertNotice notice, CancellationToken ct) {
        foreach (var observer in observers) await observer.OnUpsertAsync(notice, ct);
    }

    private async Task<ProtoVersion?> PreviousBestAsync(string platform, string build, CancellationToken ct) {
        var candidates = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.Platform == platform && p.Build != build && p.DeletedAt == null)
            .Where(p => p.Build != null && p.Build != "" && p.AppVersion != null && p.AppVersion != "")
            .ToListAsync(ct);

        return candidates
            .Select(p => (Row: p, Key: ProtoVersionQuality.LatestSortKey(p.Platform, p.Build, p.AppVersion)))
            .Where(x => x.Key != long.MinValue)
            .OrderByDescending(x => x.Key)
            .Select(x => x.Row)
            .FirstOrDefault();
    }

    public async Task<Dictionary<string, int>> SourceCountsAsync(CancellationToken ct = default) =>
        await db.ProtoVersions.GroupBy(p => p.Source)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

    public Task<List<ProtoVersion>> ListAsync(string? platform, CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .Where(p => platform == null || p.Platform == platform)
            .Where(p => p.Build != null && p.Build != "" && p.AppVersion != null && p.AppVersion != "")
            .Where(p => p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(ct);

    public Task<Dictionary<string, int>> ShaOrdersAsync(CancellationToken ct = default) =>
        db.ProtoShaOrders.AsNoTracking()
            .ToDictionaryAsync(o => o.ProtoSha, o => o.SortOrder, StringComparer.OrdinalIgnoreCase, ct);

    public async Task SetShaOrderAsync(string protoSha, int order, string? who, CancellationToken ct = default) {
        var row = await db.ProtoShaOrders.FirstOrDefaultAsync(o => o.ProtoSha == protoSha, ct);
        if (order == 0) {
            if (row is null) return;
            db.ProtoShaOrders.Remove(row);
            await db.SaveChangesAsync(ct);
            await NotifyRegistryAsync($"sha-order:{protoSha}", ct);
            return;
        }

        if (row is null) {
            row = new ProtoShaOrder { ProtoSha = protoSha };
            db.ProtoShaOrders.Add(row);
        }

        row.SortOrder = order;
        row.UpdatedAt = time.GetUtcNow();
        row.UpdatedBy = who;
        await db.SaveChangesAsync(ct);
        await NotifyRegistryAsync($"sha-order:{protoSha}", ct);
    }

    public async Task<List<MergeSuggestion>> SuggestMergesAsync(CancellationToken ct = default) {
        var rows = await db.ProtoVersions.AsNoTracking()
            .Where(p => p.DeletedAt == null && p.CanonicalId == null)
            .Where(p => p.Build != null && p.Build != "" && p.AppVersion != null && p.AppVersion != "")
            .Where(p => p.ProtoSha != null && p.ProtoSha != "")
            .Select(p => new { p.Platform, p.Build, p.AppVersion, p.ProtoSha })
            .ToListAsync(ct);

        return [
            .. rows
                .GroupBy(r => new { r.AppVersion, r.ProtoSha })
                .Where(g => g.Select(r => r.Platform).Distinct().Count() >= 2)
                .Select(g => new MergeSuggestion(g.Key.AppVersion, g.Key.ProtoSha,
                    g.Select(r => new MergeMember(r.Platform, r.Build))
                        .OrderBy(m => m.Platform).ToList()))
                .OrderBy(s => s.AppVersion)
        ];
    }

    public Task<List<ProtoVersion>> DeletedAsync(CancellationToken ct = default) =>
        db.ProtoVersions.AsNoTracking()
            .Where(p => p.DeletedAt != null)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync(ct);

    public async Task<bool> SoftDeleteAsync(string platform, string build, CancellationToken ct = default) {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return false;
        row.DeletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyRegistryAsync($"delete:{platform}:{build}", ct);
        return true;
    }

    public async Task<bool> RestoreAsync(string platform, string build, CancellationToken ct = default) {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return false;
        row.DeletedAt = null;
        row.CanonicalId = null;
        await db.SaveChangesAsync(ct);
        await NotifyRegistryAsync($"restore:{platform}:{build}", ct);
        return true;
    }

    public async Task<MetadataUpdate> UpdateMetadataAsync(
        string platform, string build, string? appVersion, string? clientVersion, string? source,
        string? newBuild = null, CancellationToken ct = default) {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return MetadataUpdate.NotFound;

        if (!string.IsNullOrWhiteSpace(newBuild) && newBuild != build) {
            bool clash = await db.ProtoVersions.AnyAsync(
                p => p.Platform == platform && p.Build == newBuild && p.Id != row.Id, ct);
            if (clash) return MetadataUpdate.BuildCollision;
            row.Build = newBuild;
        }

        if (appVersion is not null) row.AppVersion = appVersion;
        if (clientVersion is not null) row.ClientVersion = clientVersion;
        if (source is not null) row.Source = source;
        await db.SaveChangesAsync(ct);
        await NotifyRegistryAsync($"metadata:{platform}:{row.Build}", ct);
        return MetadataUpdate.Ok;
    }

    public async Task<int> MergeAsync(
        (string Platform, string Build) canonical, IReadOnlyList<(string Platform, string Build)> aliases,
        CancellationToken ct = default) {
        var canon = await db.ProtoVersions
            .FirstOrDefaultAsync(p => p.Platform == canonical.Platform && p.Build == canonical.Build, ct);
        if (canon is null) return 0;
        bool promoted = canon.CanonicalId is not null;
        if (promoted) {
            canon.CanonicalId = null;
            canon.DeletedAt = null;
        }

        int linked = 0;
        var demotedIds = new List<int>();
        foreach ((string platform, string build) in aliases) {
            if (platform == canonical.Platform && build == canonical.Build) continue;
            var alias = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
            if (alias is null || alias.Id == canon.Id) continue;
            demotedIds.Add(alias.Id);
            alias.CanonicalId = canon.Id;
            alias.DeletedAt = null;
            linked++;
        }

        var stale = await db.ProtoVersions
            .Where(p => p.CanonicalId != null
                        && (demotedIds.Contains(p.CanonicalId.Value) || p.CanonicalId == canon.Id))
            .ToListAsync(ct);
        foreach (var s in stale) {
            if (s.Id == canon.Id) continue;
            s.CanonicalId = canon.Id;
            s.DeletedAt = null;
        }

        await db.SaveChangesAsync(ct);
        if (promoted || linked > 0) await NotifyRegistryAsync($"merge:{canonical.Platform}:{canonical.Build}", ct);
        return linked;
    }

    public async Task<string?> SetProtoAsync(string platform, string build, string protoText, CancellationToken ct = default) {
        var row = await db.ProtoVersions.FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (row is null) return null;

        var norm = ProtoCanonicalForm.Normalize(protoText);
        row.ProtoSha = norm.Ok ? norm.Sha : ProtoHash.Of(protoText);
        await UpsertProtoProtoAsync(row.Id, protoText, null, row.ProtoSha, ct);
        await NotifyRegistryAsync($"proto:{platform}:{build}", ct);
        return row.ProtoSha;
    }

    public Task<ProtoProto?> GetProtoAsync(int protoVersionId, CancellationToken ct = default) =>
        db.ProtoProtos.AsNoTracking().FirstOrDefaultAsync(x => x.ProtoVersionId == protoVersionId, ct);

    public async Task<CanonicalText> GetCanonicalTextAsync(string sha, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(sha)) return new CanonicalText(false, null, null, null);

        var row = await db.ProtoCanonicals.AsNoTracking().FirstOrDefaultAsync(c => c.ProtoSha == sha, ct);
        if (row is not null) return new CanonicalText(row.Ok, row.CanonicalText, row.CanonicalSha, row.Error);

        var versionId = await db.ProtoVersions.AsNoTracking()
            .Where(v => v.ProtoSha == sha)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync(ct);
        string? raw = versionId is null
            ? null
            : await db.ProtoProtos.AsNoTracking()
                .Where(pp => pp.ProtoVersionId == versionId)
                .Select(pp => pp.ProtoText)
                .FirstOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(raw)) return new CanonicalText(false, null, null, null);

        await EnsureCanonicalAsync(sha, raw, ct);
        row = await db.ProtoCanonicals.AsNoTracking().FirstOrDefaultAsync(c => c.ProtoSha == sha, ct);
        return row is not null
            ? new CanonicalText(row.Ok, row.CanonicalText, row.CanonicalSha, row.Error)
            : new CanonicalText(false, null, null, null);
    }

    public async Task<(ProtoProto? Raw, CanonicalText Canonical)> GetCanonicalForVersionAsync(
        string platform, string build, CancellationToken ct = default) {
        var version = await db.ProtoVersions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Platform == platform && p.Build == build, ct);
        if (version is null) return (null, new CanonicalText(false, null, null, null));

        var raw = await db.ProtoProtos.AsNoTracking().FirstOrDefaultAsync(x => x.ProtoVersionId == version.Id, ct);
        if (raw is null || string.IsNullOrEmpty(version.ProtoSha)) return (raw, new CanonicalText(false, null, null, null));

        return (raw, await GetCanonicalTextAsync(version.ProtoSha, ct));
    }

    public sealed record UpsertOutcome(
        ProtoVersion Row,
        bool Created,
        bool ProtoChanged,
        VersionDelta Delta,
        string? PrevAppVersion,
        string? PrevBuild);

    public sealed record MergeSuggestion(string AppVersion, string ProtoSha, IReadOnlyList<MergeMember> Members);

    public sealed record MergeMember(string Platform, string Build);

    public sealed record CanonicalText(bool Ok, string? Text, string? Sha, string? Error);
}
