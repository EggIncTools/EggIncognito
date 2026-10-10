using EggIncognito.Models.Coverage;

namespace EggIncognito.Services.Coverage;

public static class ConsumeCoverageBuilder {
    public const int DefaultItemTarget = 200;
    public const int DefaultObservationTarget = 10;

    private static readonly CoverageTargetRow Fallback =
        new(0, null, null, null, DefaultItemTarget, DefaultObservationTarget, true);

    public static ConsumeCoverageMap Build(IReadOnlyList<CoverageCatalogCell> catalog,
        IReadOnlyDictionary<string, CoverageFamilyInfo> families,
        IEnumerable<CoverageSample> samples, IReadOnlyList<CoverageTargetRow> targets) {
        var tallies = new Dictionary<(string, string, string), Tally>();
        foreach (var s in samples) {
            var key = (s.SpecName, s.Level, s.Rarity);
            var t = tallies.GetValueOrDefault(key);
            tallies[key] = t with { Items = t.Items + s.Quantity, Observations = t.Observations + 1 };
        }

        var built = catalog
            .Where(c => families.ContainsKey(c.SpecName))
            .GroupBy(c => c.SpecName, StringComparer.Ordinal)
            .Select(g => BuildFamily(families[g.Key], g, tallies, targets))
            .OrderBy(f => f.Order)
            .Select(f => f.Family)
            .ToList();

        var scoped = built.SelectMany(f => f.Tiers).SelectMany(t => t.Cells).Where(c => c.InScope).ToList();
        return new ConsumeCoverageMap(
            scoped.Count == 0 ? 0 : scoped.Average(c => c.Validity),
            scoped.Count,
            scoped.Count(IsComplete),
            scoped.Sum(c => c.Items),
            scoped.Sum(c => c.Observations),
            built);
    }

    public static double Validity(int items, int observations, int itemTarget, int observationTarget) {
        double item = itemTarget <= 0 ? 1 : items / (double)itemTarget;
        double obs = observationTarget <= 1
            ? observations >= 1 ? 1 : 0
            : Math.Max(0, observations - 1) / (double)(observationTarget - 1);
        return Math.Clamp(Math.Min(item, obs), 0, 1);
    }

    private static (int Order, CoverageFamily Family) BuildFamily(CoverageFamilyInfo info,
        IEnumerable<CoverageCatalogCell> rows, Dictionary<(string, string, string), Tally> tallies,
        IReadOnlyList<CoverageTargetRow> targets) {
        var tiers = rows
            .GroupBy(r => (r.Level, r.AfxLevel))
            .OrderBy(g => g.Key.AfxLevel)
            .Select(g => new CoverageTier(g.Key.Level, g.Key.AfxLevel,
                g.Key.AfxLevel >= 0 && g.Key.AfxLevel < info.TierNames.Count ? info.TierNames[g.Key.AfxLevel] : g.Key.Level,
                [.. g.OrderBy(c => c.AfxRarity).Select(c => BuildCell(c, tallies, targets))]))
            .ToList();

        var scoped = tiers.SelectMany(t => t.Cells).Where(c => c.InScope).ToList();
        return (info.Order, new CoverageFamily(
            info.SpecName,
            info.PluralName,
            scoped.Count == 0 ? 0 : scoped.Average(c => c.Validity),
            scoped.Count,
            scoped.Count(IsComplete),
            scoped.Sum(c => c.Items),
            scoped.Sum(c => c.Observations),
            tiers));
    }

    private static CoverageCell BuildCell(CoverageCatalogCell c, Dictionary<(string, string, string), Tally> tallies,
        IReadOnlyList<CoverageTargetRow> targets) {
        var t = tallies.GetValueOrDefault((c.SpecName, c.Level, c.Rarity));
        var resolved = Resolve(c, targets);
        long? cellTarget = targets.FirstOrDefault(r => r.SpecName == c.SpecName && r.Level == c.Level
                                                       && r.Rarity == c.Rarity)?.Id;
        int itemsShort = Math.Max(0, resolved.ItemTarget - t.Items);
        int obsShort = Math.Max(0, resolved.ObservationTarget - t.Observations);
        int batch = obsShort == 0 ? itemsShort : Math.Max(1, (int)Math.Ceiling(itemsShort / (double)obsShort));
        return new CoverageCell(c.Rarity, c.AfxRarity, t.Items, t.Observations,
            resolved.ItemTarget, resolved.ObservationTarget, resolved.Enabled,
            Validity(t.Items, t.Observations, resolved.ItemTarget, resolved.ObservationTarget),
            itemsShort, obsShort, batch, resolved.Id, cellTarget);
    }

    private static CoverageTargetRow Resolve(CoverageCatalogCell c, IReadOnlyList<CoverageTargetRow> targets) =>
        targets
            .Where(r => (r.SpecName is null || r.SpecName == c.SpecName) && (r.Level is null || r.Level == c.Level)
                                                                       && (r.Rarity is null || r.Rarity == c.Rarity))
            .MaxBy(Specificity) ?? Fallback;

    private static int Specificity(CoverageTargetRow r) =>
        (r.SpecName is null ? 0 : 4) + (r.Level is null ? 0 : 2) + (r.Rarity is null ? 0 : 1);

    private static bool IsComplete(CoverageCell c) => c.InScope && c.Validity >= 1;

    private readonly record struct Tally(int Items, int Observations);
}
