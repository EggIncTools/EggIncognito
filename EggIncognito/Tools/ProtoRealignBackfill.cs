using System.Text.Json;
using EggIncognito.Core.Services;
using EggIncognito.Core.Services.ProtoExtract;
using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Tools;

public static class ProtoRealignBackfill {
    public sealed record RowOutcome(string Kind, string Key, string? OldSha, string? NewSha, string Status, string? Error);

    public sealed record Report(int Scanned, int Updated, int AlreadyCanonical, int Failed, int RemappedAnalyzedFiles, List<RowOutcome> Rows);

    private static bool MessageIndexMatches(string? stored, IReadOnlyList<string> names) {
        if (stored is null) return false;
        List<string>? existing;
        try {
            existing = JsonSerializer.Deserialize<List<string>>(stored);
        } catch (JsonException) {
            return false;
        }

        return existing is not null && existing.SequenceEqual(names);
    }

    public static async Task<Report> RunAsync(EggIncognitoDbContext db, bool dryRun, CancellationToken ct) {
        int scanned = 0, updated = 0, alreadyCanonical = 0, failed = 0, remapped = 0;
        var failedRows = new List<RowOutcome>();
        var updatedRows = new List<RowOutcome>();
        var shaMap = new Dictionary<string, string>();

        var registryVersionIds = await db.ProtoProtos.AsNoTracking()
            .Where(p => p.ProtoText != "")
            .Select(p => p.ProtoVersionId)
            .ToListAsync(ct);

        foreach (int versionId in registryVersionIds) {
            var pp = await db.ProtoProtos.FirstOrDefaultAsync(p => p.ProtoVersionId == versionId, ct);
            if (pp is null) continue;
            var version = await db.ProtoVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);
            if (version is null) {
                db.ChangeTracker.Clear();
                continue;
            }

            string key = $"{version.Platform}/{version.Build}";
            string oldSha = version.ProtoSha;
            var norm = ProtoCanonicalForm.Normalize(pp.ProtoText);
            if (!norm.Ok) {
                scanned++;
                failed++;
                failedRows.Add(new RowOutcome("registry", key, oldSha, null, "failed", norm.Error));
                db.ChangeTracker.Clear();
                continue;
            }

            var names = ProtoTextIndex.Names(norm.Text);
            string newMessageIndex = JsonSerializer.Serialize(names);
            if (norm.Sha == version.ProtoSha && norm.Text == pp.ProtoText && MessageIndexMatches(pp.MessageIndex, names)) {
                scanned++;
                alreadyCanonical++;
                db.ChangeTracker.Clear();
                continue;
            }

            if (!string.IsNullOrEmpty(oldSha) && oldSha != norm.Sha) shaMap[oldSha] = norm.Sha;

            if (!dryRun) {
                version.ProtoSha = norm.Sha;
                pp.ProtoText = norm.Text;
                pp.MessageIndex = newMessageIndex;
                try {
                    await db.SaveChangesAsync(ct);
                } catch (DbUpdateException ex) {
                    scanned++;
                    failed++;
                    failedRows.Add(new RowOutcome("registry", key, oldSha, norm.Sha, "failed", ex.Message));
                    db.ChangeTracker.Clear();
                    continue;
                }
            }

            scanned++;
            updated++;
            updatedRows.Add(new RowOutcome("registry", key, oldSha, norm.Sha, "updated", null));
            db.ChangeTracker.Clear();
        }

        var stagedIds = await db.StagedProtos.AsNoTracking()
            .Where(s => s.ProtoText != "")
            .Select(s => s.Id)
            .ToListAsync(ct);
        foreach (int id in stagedIds) {
            var row = await db.StagedProtos.FirstOrDefaultAsync(s => s.Id == id, ct);
            if (row is null) continue;

            string key = $"staged:{id}";
            string oldSha = row.ProtoSha;
            var norm = ProtoCanonicalForm.Normalize(row.ProtoText);
            if (!norm.Ok) {
                scanned++;
                failed++;
                failedRows.Add(new RowOutcome("staged", key, oldSha, null, "failed", norm.Error));
                db.ChangeTracker.Clear();
                continue;
            }

            var names = ProtoTextIndex.Names(norm.Text);
            string newMessageIndex = JsonSerializer.Serialize(names);
            if (norm.Sha == row.ProtoSha && norm.Text == row.ProtoText && MessageIndexMatches(row.MessageIndex, names)) {
                scanned++;
                alreadyCanonical++;
                db.ChangeTracker.Clear();
                continue;
            }

            if (!string.IsNullOrEmpty(oldSha) && oldSha != norm.Sha) shaMap[oldSha] = norm.Sha;

            if (!dryRun) {
                row.ProtoSha = norm.Sha;
                row.ProtoText = norm.Text;
                row.MessageIndex = newMessageIndex;
                try {
                    await db.SaveChangesAsync(ct);
                } catch (DbUpdateException ex) {
                    scanned++;
                    failed++;
                    failedRows.Add(new RowOutcome("staged", key, oldSha, norm.Sha, "failed", ex.Message));
                    db.ChangeTracker.Clear();
                    continue;
                }
            }

            scanned++;
            updated++;
            updatedRows.Add(new RowOutcome("staged", key, oldSha, norm.Sha, "updated", null));
            db.ChangeTracker.Clear();
        }

        if (shaMap.Count > 0) {
            var oldShas = shaMap.Keys.ToList();
            var analyzedIds = await db.AnalyzedFiles.AsNoTracking()
                .Where(a => a.ProtoSha != null && oldShas.Contains(a.ProtoSha))
                .Select(a => a.FileSha)
                .ToListAsync(ct);

            foreach (string fileSha in analyzedIds) {
                if (!dryRun) {
                    var af = await db.AnalyzedFiles.FirstOrDefaultAsync(a => a.FileSha == fileSha, ct);
                    if (af?.ProtoSha is null || !shaMap.TryGetValue(af.ProtoSha, out string? newSha)) {
                        db.ChangeTracker.Clear();
                        continue;
                    }
                    af.ProtoSha = newSha;
                    try {
                        await db.SaveChangesAsync(ct);
                    } catch (DbUpdateException) {
                        db.ChangeTracker.Clear();
                        continue;
                    }
                    db.ChangeTracker.Clear();
                }

                remapped++;
            }
        }

        var rows = new List<RowOutcome>(failedRows.Count + Math.Min(updatedRows.Count, 200));
        rows.AddRange(failedRows);
        rows.AddRange(updatedRows.Take(200));

        return new Report(scanned, updated, alreadyCanonical, failed, remapped, rows);
    }
}
