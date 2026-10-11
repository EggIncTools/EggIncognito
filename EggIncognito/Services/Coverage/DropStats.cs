using EggIncognito.Services.Devices;

namespace EggIncognito.Services.Coverage;

public sealed record DropBucket(int Count, int Observations, double Share);

public sealed record DropRow(
    string Name,
    string Level,
    string Rarity,
    int Observations,
    double Share,
    int Min,
    int Max,
    IReadOnlyList<DropBucket> Buckets);

public sealed record DropSummary(string Family, string Level, string Rarity, int Observations, IReadOnlyList<DropRow> Rows);

public sealed class DropStats(IArtifactObservationQuery observations) {
    public async Task<DropSummary> ForCellAsync(string family, string level, string rarity, CancellationToken ct) {
        var rows = await observations.ConsumeByproductsAsync(family, level, rarity, ct);
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
                    missing > 0 ? 0 : counts.Min(), counts.Max(), buckets);
            })
            .OrderByDescending(r => r.Observations)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();
        return new DropSummary(family, level, rarity, total, result);
    }
}
