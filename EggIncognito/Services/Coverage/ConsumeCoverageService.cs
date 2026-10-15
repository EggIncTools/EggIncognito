using EggSharp.GameData.Artifacts;
using EggIncognito.Data.Models;
using EggIncognito.Data.Services;
using EggSharp.GameData;
using EggIncognito.Models.Coverage;
using EggIncognito.Services.Artifacts;
using EggIncognito.Services.DataApi;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Coverage;

public sealed class ConsumeCoverageService(EggIncognitoDbContext db, GameDataStore gameData, TimeProvider time) {
    public async Task<ConsumeCoverageMap?> MapAsync(CancellationToken ct) {
        if (gameData.Doc(ArtifactCatalog.DocumentId) is not { } json) return null;

        var catalog = ArtifactCatalog.Parse(json).Artifacts
            .Select(e => new CoverageCatalogCell(e.SpecName, e.Level,
                ArtifactTreeBuilder.IsFragment(e.SpecName) ? -1 : e.AfxLevel, e.Rarity, e.AfxRarity))
            .ToList();

        var byName = ArtifactDefinitions.All.ToDictionary(f => f.Name, StringComparer.Ordinal);
        var families = new Dictionary<string, CoverageFamilyInfo>(StringComparer.Ordinal);
        foreach (var f in ArtifactDefinitions.All.Where(f => ConsumeCoverageKinds.Consumable(f.Kind))) {
            var info = new CoverageFamilyInfo(f.Name, ConsumeCoverageKinds.DisplayName(f.Kind, f.Name, f.PluralName),
                f.Kind, f.Order, [.. f.Tiers.OrderBy(t => t.Level).Select(t => t.Name)]);
            families[f.Name] = info;
            if (f.Kind == ArtifactKind.Stone && byName.TryGetValue(f.Name + "_FRAGMENT", out var fragment)) {
                families[fragment.Name] = info;
            }
        }

        var device = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
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
