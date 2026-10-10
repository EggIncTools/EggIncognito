using EggIncognito.Artifacts;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggIncognito.GameData;
using EggIncognito.Models.Coverage;
using EggIncognito.Services.DataApi;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Coverage;

public sealed class ConsumeCoverageService(EggIncognitoDbContext db, GameDataStore gameData, TimeProvider time) {
    private const string ConsumeAction = "consume";

    public async Task<ConsumeCoverageMap?> MapAsync(CancellationToken ct) {
        if (gameData.Doc(ArtifactCatalog.DocumentId) is not { } json) return null;

        var catalog = ArtifactCatalog.Parse(json).Artifacts
            .Select(e => new CoverageCatalogCell(e.SpecName, e.Level, e.AfxLevel, e.Rarity, e.AfxRarity))
            .ToList();
        var families = ArtifactDefinitions.All
            .Where(f => f.Kind == ArtifactKind.Artifact)
            .ToDictionary(f => f.Name,
                f => new CoverageFamilyInfo(f.Name, f.PluralName, f.Order,
                    [.. f.Tiers.OrderBy(t => t.Level).Select(t => t.Name)]),
                StringComparer.Ordinal);

        var device = await db.ArtifactConsumeObservations.AsNoTracking()
            .Where(o => o.Action == ConsumeAction && o.Success)
            .Select(o => new { o.SpecName, o.SpecLevel, o.SpecRarity, o.CountRequested })
            .ToListAsync(ct);
        var targets = await db.ConsumeCoverageTargets.AsNoTracking()
            .Select(t => new CoverageTargetRow(t.Id, t.SpecName, t.SpecLevel, t.SpecRarity, t.ItemTarget,
                t.ObservationTarget, t.Enabled))
            .ToListAsync(ct);

        var samples = device.Select(o => new CoverageSample(o.SpecName, o.SpecLevel, o.SpecRarity, Math.Max(o.CountRequested, 1)));
        return ConsumeCoverageBuilder.Build(catalog, families, samples, targets);
    }

    public async Task<ConsumeCoverageTarget> UpsertAsync(CoverageTargetRequest req, string? by, CancellationToken ct) {
        var row = await db.ConsumeCoverageTargets.FirstOrDefaultAsync(
            t => t.SpecName == req.SpecName && t.SpecLevel == req.Level && t.SpecRarity == req.Rarity, ct);
        if (row is null) {
            row = new ConsumeCoverageTarget { SpecName = req.SpecName, SpecLevel = req.Level, SpecRarity = req.Rarity };
            db.ConsumeCoverageTargets.Add(row);
        }

        row.ItemTarget = req.ItemTarget;
        row.ObservationTarget = req.ObservationTarget;
        row.Enabled = req.Enabled;
        row.UpdatedAt = time.GetUtcNow();
        row.UpdatedBy = by;
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<bool?> DeleteAsync(long id, CancellationToken ct) {
        var row = await db.ConsumeCoverageTargets.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (row is null) return null;
        if (row.SpecName is null && row.SpecLevel is null && row.SpecRarity is null) return false;
        db.ConsumeCoverageTargets.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }
}
