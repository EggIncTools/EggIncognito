using System.Data.Common;
using EggIncognito.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Data.Services;

public sealed class UserThemeStore(EggIncognitoDbContext db, TimeProvider time) {
    private const string UniqueViolation = "23505";

    public async Task<IReadOnlyList<UserTheme>> ByOwnerAsync(Guid owner, CancellationToken ct = default) =>
        await db.UserThemes.AsNoTracking()
            .Where(t => t.OwnerUserId == owner)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public async Task<UserTheme?> GetAsync(Guid owner, string slug, CancellationToken ct = default) =>
        await db.UserThemes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OwnerUserId == owner && t.Slug == slug, ct);

    public async Task<UserTheme?> GetByIdAsync(long id, CancellationToken ct = default) =>
        await db.UserThemes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<UserTheme?> ActiveForAsync(Guid owner, CancellationToken ct = default) =>
        await db.UserThemes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.OwnerUserId == owner && t.IsActive, ct);

    public async Task<UserTheme> UpsertAsync(Guid owner, string slug, string name, int schemaVersion, string model,
        CancellationToken ct = default) {
        var existing = await db.UserThemes.FirstOrDefaultAsync(t => t.OwnerUserId == owner && t.Slug == slug, ct);
        if (existing is not null) {
            existing.Name = name;
            existing.SchemaVersion = schemaVersion;
            existing.Model = model;
            existing.ValidatedAt = null;
            existing.Validation = null;
            existing.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var created = new UserTheme {
            OwnerUserId = owner,
            Slug = slug,
            Name = name,
            SchemaVersion = schemaVersion,
            Model = model,
            UpdatedAt = time.GetUtcNow()
        };
        db.UserThemes.Add(created);
        try {
            await db.SaveChangesAsync(ct);
            return created;
        } catch (DbUpdateException ex) when (IsUniqueViolation(ex)) {
            db.Entry(created).State = EntityState.Detached;
            var row = await db.UserThemes.FirstAsync(t => t.OwnerUserId == owner && t.Slug == slug, ct);
            row.Name = name;
            row.SchemaVersion = schemaVersion;
            row.Model = model;
            row.ValidatedAt = null;
            row.Validation = null;
            row.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return row;
        }
    }

    public async Task<bool> DeleteAsync(Guid owner, string slug, CancellationToken ct = default) {
        var row = await db.UserThemes.FirstOrDefaultAsync(t => t.OwnerUserId == owner && t.Slug == slug, ct);
        if (row is null) return false;
        db.UserThemes.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ActivateAsync(Guid owner, string slug, string validationJson,
        CancellationToken ct = default) {
        var row = await db.UserThemes.FirstOrDefaultAsync(t => t.OwnerUserId == owner && t.Slug == slug, ct);
        if (row is null) return false;
        var current = await db.UserThemes
            .Where(t => t.OwnerUserId == owner && t.IsActive && t.Slug != slug)
            .ToListAsync(ct);
        foreach (var other in current) other.IsActive = false;
        row.IsActive = true;
        row.ValidatedAt = time.GetUtcNow();
        row.Validation = validationJson;
        row.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeactivateAsync(Guid owner, CancellationToken ct = default) {
        var rows = await db.UserThemes.Where(t => t.OwnerUserId == owner && t.IsActive).ToListAsync(ct);
        if (rows.Count == 0) return false;
        foreach (var row in rows) {
            row.IsActive = false;
            row.UpdatedAt = time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<SiteThemePolicy> GetPolicyAsync(CancellationToken ct = default) =>
        await db.SiteThemePolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == 1, ct)
        ?? new SiteThemePolicy { Id = 1, CustomCssEnabled = true };

    public async Task<SiteThemePolicy> SetPolicyAsync(bool customCssEnabled, long? defaultThemeId, Guid? updatedBy,
        CancellationToken ct = default) {
        var row = await db.SiteThemePolicies.FirstOrDefaultAsync(p => p.Id == 1, ct);
        if (row is null) {
            row = new SiteThemePolicy { Id = 1 };
            db.SiteThemePolicies.Add(row);
        }

        row.CustomCssEnabled = customCssEnabled;
        row.DefaultThemeId = defaultThemeId;
        row.UpdatedAt = time.GetUtcNow();
        row.UpdatedByUserId = updatedBy;
        try {
            await db.SaveChangesAsync(ct);
        } catch (DbUpdateException ex) when (IsUniqueViolation(ex)) {
            db.ChangeTracker.Clear();
            var existing = await db.SiteThemePolicies.FirstAsync(p => p.Id == 1, ct);
            existing.CustomCssEnabled = customCssEnabled;
            existing.DefaultThemeId = defaultThemeId;
            existing.UpdatedAt = time.GetUtcNow();
            existing.UpdatedByUserId = updatedBy;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        return row;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: UniqueViolation };
}
