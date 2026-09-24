using System.Data.Common;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class EnvDesignStore(EggIncognitoDbContext db, TimeProvider time) {
    private const int VersionSaveAttempts = 4;
    private const string UniqueViolation = "23505";

    public enum DeleteResult {
        Ok,
        NotFound,
        Forbidden
    }

    public enum RollbackResult {
        Ok,
        NoDesign,
        NoVersion
    }

    public sealed record DesignRow(string Name, DateTimeOffset UpdatedAt, Guid? Owner);

    public sealed record VersionRow(int VersionNo, string? Note, DateTimeOffset CreatedAt, Guid? AuthorUserId,
        int? RolledBackFrom);

    public Task<List<DesignRow>> ListAsync(CancellationToken ct) =>
        db.EnvDesigns.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new DesignRow(d.Name, d.UpdatedAt, d.OwnerUserId))
            .ToListAsync(ct);

    public Task<EnvDesign?> GetAsync(string name, CancellationToken ct) =>
        db.EnvDesigns.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name, ct);

    public async Task<int> SaveAsync(string name, string payload, Guid? ownerUserId, string? note,
        CancellationToken ct) {
        var design = await UpsertAsync(name, payload, ownerUserId, ct);
        return await AddVersionAsync(new EnvDesignVersion {
            DesignId = design.Id,
            Payload = payload,
            AuthorUserId = ownerUserId,
            Note = note
        }, ct);
    }

    public async Task<(EnvDesign? Design, List<VersionRow> Versions)> VersionsAsync(string name,
        CancellationToken ct) {
        var design = await db.EnvDesigns.AsNoTracking().FirstOrDefaultAsync(d => d.Name == name, ct);
        if (design is null) return (null, []);
        var rows = await db.EnvDesignVersions.AsNoTracking()
            .Where(v => v.DesignId == design.Id)
            .OrderByDescending(v => v.VersionNo)
            .Select(v => new VersionRow(v.VersionNo, v.Note, v.CreatedAt, v.AuthorUserId, v.RolledBackFrom))
            .ToListAsync(ct);
        return (design, rows);
    }

    public async Task<(RollbackResult Result, int FromVersion, int NewVersion)> RollbackAsync(
        string name, int versionNo, Guid? authorUserId, CancellationToken ct) {
        var design = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, ct);
        if (design is null) return (RollbackResult.NoDesign, 0, 0);
        var src = await db.EnvDesignVersions
            .FirstOrDefaultAsync(v => v.DesignId == design.Id && v.VersionNo == versionNo, ct);
        if (src is null) return (RollbackResult.NoVersion, 0, 0);

        design.Payload = src.Payload;
        design.UpdatedAt = time.GetUtcNow();
        int next = await AddVersionAsync(new EnvDesignVersion {
            DesignId = design.Id,
            Payload = src.Payload,
            AuthorUserId = authorUserId,
            RolledBackFrom = src.VersionNo,
            Note = $"rolled back to v{src.VersionNo}"
        }, ct);
        return (RollbackResult.Ok, src.VersionNo, next);
    }

    public async Task<DeleteResult> DeleteAsync(string name, Guid? callerUserId, bool isAdmin, CancellationToken ct) {
        var row = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, ct);
        if (row is null) return DeleteResult.NotFound;
        if (row.OwnerUserId != callerUserId && !isAdmin) return DeleteResult.Forbidden;
        db.EnvDesigns.Remove(row);
        await db.SaveChangesAsync(ct);
        return DeleteResult.Ok;
    }

    private async Task<EnvDesign> UpsertAsync(string name, string payload, Guid? ownerUserId, CancellationToken ct) {
        var existing = await db.EnvDesigns.FirstOrDefaultAsync(d => d.Name == name, ct);
        if (existing is not null) {
            existing.Payload = payload;
            existing.UpdatedAt = time.GetUtcNow();
            return existing;
        }

        var created = new EnvDesign { Name = name, Payload = payload, OwnerUserId = ownerUserId };
        db.EnvDesigns.Add(created);
        try {
            await db.SaveChangesAsync(ct);
            return created;
        } catch (DbUpdateException ex) when (IsUniqueViolation(ex)) {
            db.Entry(created).State = EntityState.Detached;
            return await db.EnvDesigns.FirstAsync(d => d.Name == name, ct);
        }
    }

    private async Task<int> AddVersionAsync(EnvDesignVersion version, CancellationToken ct) {
        int attempt = 0;
        while (true) {
            attempt++;
            version.VersionNo = await NextVersionNo(version.DesignId, ct);
            db.EnvDesignVersions.Add(version);
            try {
                await db.SaveChangesAsync(ct);
                return version.VersionNo;
            } catch (DbUpdateException ex) when (attempt < VersionSaveAttempts && IsUniqueViolation(ex)) {
                db.Entry(version).State = EntityState.Detached;
            }
        }
    }

    private async Task<int> NextVersionNo(long designId, CancellationToken ct) {
        int max = await db.EnvDesignVersions.AsNoTracking()
            .Where(v => v.DesignId == designId)
            .MaxAsync(v => (int?)v.VersionNo, ct) ?? 0;
        return max + 1;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: UniqueViolation };
}
