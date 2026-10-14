using EggIncognito.Models.Observations;
using EggIncognito.Services.Devices;

namespace EggIncognito.Services.Coverage;

public sealed record DropBucket(int Count, int Observations, double Share);

public sealed record DropRow(
    string Name,
    string Level,
    string Rarity,
    int Observations,
    double Share,
    double Mean,
    int Min,
    int Max,
    IReadOnlyList<DropBucket> Buckets);

public sealed record GoldBucket(double Amount, int Observations, double Share);

public sealed record GoldStats(int Observations, double Share, double Mean, double Min, double Max,
    IReadOnlyList<GoldBucket> Buckets);

public sealed record DropSummary(string Family, string Level, string Rarity, int Observations, IReadOnlyList<DropRow> Rows,
    GoldStats? Gold = null, GoldStats? Piggy = null);

public sealed class DropStats(IArtifactObservationQuery observations) {
    public async Task<DropSummary> ForCellAsync(string family, string level, string rarity, CancellationToken ct) {
        var summary = Summarize(family, level, rarity, await observations.ConsumeByproductsAsync(family, level, rarity, ct));
        var gold = SummarizeGold(await observations.ConsumeGoldenEggsAsync(family, level, rarity, ct));
        var piggy = SummarizeGold(await observations.ConsumePiggyFillAsync(family, level, rarity, ct));
        return summary with { Gold = gold, Piggy = piggy };
    }

    public async Task<IReadOnlyList<(string Family, string Level, string Rarity, DropRow Hit)>> SourcesOfAsync(
        string name, string level, string rarity, CancellationToken ct) {
        var totals = await observations.ConsumeEventCountsAsync(ct);
        var dropping = await observations.ConsumesDroppingAsync(name, ct);
        var result = new List<(string, string, string, DropRow)>();
        foreach (var cell in dropping.GroupBy(d => (d.Family, d.Level, d.Rarity))) {
            var matched = cell.Where(d => d.Byproducts.Any(b => b.Name == name && b.Level == level && b.Rarity == rarity))
                .Select(d => d.Byproducts).ToList();
            if (matched.Count == 0) continue;
            int total = Math.Max(totals.GetValueOrDefault(cell.Key), matched.Count);
            var padded = matched.Concat(Enumerable.Repeat<IReadOnlyList<ArtifactByproductRow>>([], total - matched.Count)).ToList();
            var hit = Summarize(cell.Key.Family, cell.Key.Level, cell.Key.Rarity, padded).Rows
                .First(r => r.Name == name && r.Level == level && r.Rarity == rarity);
            result.Add((cell.Key.Family, cell.Key.Level, cell.Key.Rarity, hit));
        }

        return result;
    }

    public static GoldStats? SummarizeGold(IReadOnlyList<double> amounts) {
        int total = amounts.Count;
        int hits = amounts.Count(a => a > 0);
        if (total == 0 || hits == 0) return null;

        var buckets = amounts.GroupBy(a => a).OrderBy(g => g.Key)
            .Select(g => new GoldBucket(g.Key, g.Count(), (double)g.Count() / total)).ToList();
        return new GoldStats(hits, (double)hits / total, amounts.Average(), amounts.Min(), amounts.Max(), buckets);
    }

    public static DropSummary Summarize(string family, string level, string rarity,
        IReadOnlyList<IReadOnlyList<ArtifactByproductRow>> rows) {
        if (rows.Count == 0) return new DropSummary(family, level, rarity, 0, []);

        var perDrop = new Dictionary<(string Name, string Level, string Rarity), List<int>>();
        foreach (var byproducts in rows) {
            var seen = new Dictionary<(string, string, string), int>();
            foreach (var b in byproducts) {
                var key = (b.Name, b.Level, b.Rarity);
                seen[key] = seen.GetValueOrDefault(key) + Math.Max(1, b.Count);
            }

            foreach (var (key, count) in seen) {
                if (!perDrop.TryGetValue(key, out var list)) perDrop[key] = list = [];
                list.Add(count);
            }
        }

        int total = rows.Count;
        var result = perDrop
            .Select(kv => {
                var counts = kv.Value;
                int missing = total - counts.Count;
                var buckets = counts.GroupBy(c => c).OrderBy(g => g.Key)
                    .Select(g => new DropBucket(g.Key, g.Count(), (double)g.Count() / total)).ToList();
                if (missing > 0) buckets.Insert(0, new DropBucket(0, missing, (double)missing / total));
                return new DropRow(kv.Key.Name, kv.Key.Level, kv.Key.Rarity, counts.Count, (double)counts.Count / total,
                    counts.Sum() / (double)total, missing > 0 ? 0 : counts.Min(), counts.Max(), buckets);
            })
            .OrderByDescending(r => r.Observations)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        return new DropSummary(family, level, rarity, total, result);
    }
}
