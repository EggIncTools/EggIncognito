using System.Text.Json;
using System.Text.Json.Nodes;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Services.Coverage;
using EggIncognito.Services.Devices;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.DataApi;

public static class ArtifactObservationSource {
    private static readonly JsonSerializerOptions IndentedJson = JsonPresets.Indented;
    private static readonly Lock Gate = new();
    private static (int Count, long MaxId) _stamp = (-1, -1);
    private static string? _cached;

    public static async Task<DataPayload?> ProduceAsync(DataProduceContext ctx, CancellationToken ct) {
        if (ctx.Services.GetService(typeof(EggIncognitoDbContext)) is not EggIncognitoDbContext db) return null;

        var device = await db.ArtifactConsumeObservations.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), MaxId = g.Max(o => o.Id) })
            .FirstOrDefaultAsync(ct);

        var stamp = (device?.Count ?? 0, device?.MaxId ?? 0L);

        lock (Gate) {
            if (_stamp == stamp && _cached is not null) return DataPayload.Json(_cached);
        }

        string json = await BuildAsync(db, ct);
        lock (Gate) {
            _stamp = stamp;
            _cached = json;
        }

        return DataPayload.Json(json);
    }

    private static async Task<string> BuildAsync(EggIncognitoDbContext db, CancellationToken ct) {
        var rows = await db.ArtifactConsumeObservations.AsNoTracking()
            .Where(ConsumeDropFilter.Counted)
            .Select(o => new Observation(
                o.Action, o.SpecName, o.SpecLevel, o.SpecRarity, o.Byproducts, o.GoldenEggs,
                o.CountRequested, o.RarityAchieved, o.GoldPricePaid, o.CraftingCount))
            .ToListAsync(ct);

        var doc = new JsonObject {
            ["groups"] = Aggregate(rows),
            ["totalSamples"] = rows.Count,
            ["provenance"] = new JsonObject {
                ["groups"] = new JsonObject {
                    ["origin"] = "observed",
                    ["locator"] = "device harvest",
                    ["method"] = "captured"
                }
            }
        };
        return doc.ToJsonString(IndentedJson);
    }

    private static JsonArray Aggregate(List<Observation> rows) {
        var groups = new JsonArray();
        foreach (var group in rows
                     .GroupBy(o => (o.SpecName, o.SpecLevel, o.SpecRarity, o.Action))
                     .OrderBy(g => g.Key.SpecName, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.SpecLevel, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.SpecRarity, StringComparer.Ordinal)
                     .ThenBy(g => g.Key.Action, StringComparer.Ordinal)) {
            var frequency = new Dictionary<string, (int Occurrences, int Total)>(StringComparer.Ordinal);
            foreach (var row in group) {
                foreach ((string key, int count) in Byproducts(row.Byproducts)) {
                    (int occurrences, int total) = frequency.GetValueOrDefault(key);
                    frequency[key] = (occurrences + 1, total + count);
                }
            }

            var drops = DropStats.Summarize(group.Key.SpecName, group.Key.SpecLevel, group.Key.SpecRarity,
                    [.. group.Select(r => ArtifactObservationQuery.ParseByproducts(r.Byproducts))])
                .Rows.ToDictionary(r => $"{r.Name}/{r.Level}/{r.Rarity}", StringComparer.Ordinal);

            var byproducts = new JsonArray([.. frequency
                .OrderByDescending(f => f.Value.Total)
                .Select(f => {
                    var entry = new JsonObject {
                        ["byproduct"] = f.Key,
                        ["occurrences"] = f.Value.Occurrences,
                        ["totalCount"] = f.Value.Total
                    };
                    if (drops.TryGetValue(f.Key, out var d)) {
                        entry["mean"] = d.Mean;
                        entry["share"] = d.Share;
                        entry["min"] = d.Min;
                        entry["max"] = d.Max;
                        entry["buckets"] = new JsonArray([.. d.Buckets.Select(b => (JsonNode)new JsonObject {
                            ["count"] = b.Count,
                            ["observations"] = b.Observations,
                            ["share"] = b.Share
                        })]);
                    }

                    return (JsonNode)entry;
                })]);

            var rarityOutcomes = new JsonArray([.. group
                .Where(g => g.RarityAchieved is not null)
                .GroupBy(g => g.RarityAchieved!, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .Select(outcome => (JsonNode)new JsonObject {
                    ["rarity"] = outcome.Key,
                    ["count"] = outcome.Count()
                })]);

            double[] goldenEggs = [.. group.Select(g => g.GoldenEggs)];
            var gold = DropStats.TracksGold(group.Key.SpecName) ? DropStats.SummarizeGold(goldenEggs) : null;
            double[] pricesPaid = [.. group.Where(g => g.GoldPricePaid is not null).Select(g => g.GoldPricePaid!.Value)];
            groups.Add(new JsonObject {
                ["specName"] = group.Key.SpecName,
                ["level"] = group.Key.SpecLevel,
                ["rarity"] = group.Key.SpecRarity,
                ["action"] = group.Key.Action,
                ["samples"] = group.Count(),
                ["itemsConsumed"] = group.Sum(g => g.CountRequested),
                ["goldenEggs"] = new JsonObject {
                    ["mean"] = goldenEggs.Average(),
                    ["min"] = goldenEggs.Min(),
                    ["max"] = goldenEggs.Max(),
                    ["share"] = gold?.Share ?? 0,
                    ["buckets"] = gold is null
                        ? null
                        : new JsonArray([.. gold.Buckets.Select(b => (JsonNode)new JsonObject {
                            ["amount"] = b.Amount,
                            ["observations"] = b.Observations,
                            ["share"] = b.Share
                        })])
                },
                ["byproducts"] = byproducts,
                ["rarityOutcomes"] = rarityOutcomes,
                ["goldPricePaid"] = pricesPaid.Length == 0
                    ? null
                    : new JsonObject {
                        ["min"] = pricesPaid.Min(),
                        ["max"] = pricesPaid.Max(),
                        ["minCraftingCount"] = group.Where(g => g.CraftingCount is not null)
                            .Min(g => g.CraftingCount!.Value),
                        ["maxCraftingCount"] = group.Where(g => g.CraftingCount is not null)
                            .Max(g => g.CraftingCount!.Value)
                    }
            });
        }

        return groups;
    }

    private static IEnumerable<(string Key, int Count)> Byproducts(string json) {
        JsonNode? parsed;
        try {
            parsed = JsonNode.Parse(json);
        } catch (JsonException) {
            yield break;
        }

        if (parsed is not JsonArray array) yield break;
        foreach (var node in array) {
            if (node is not JsonObject row) continue;
            string? name = row["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;
            string key = $"{name}/{row["level"]?.GetValue<string>()}/{row["rarity"]?.GetValue<string>()}";
            yield return (key, row["count"]?.GetValue<int>() ?? 1);
        }
    }

    private sealed record Observation(
        string Action,
        string SpecName,
        string SpecLevel,
        string SpecRarity,
        string Byproducts,
        double GoldenEggs,
        int CountRequested,
        string? RarityAchieved,
        double? GoldPricePaid,
        int? CraftingCount);
}
