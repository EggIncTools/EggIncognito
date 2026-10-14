using System.Globalization;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Coverage;

public sealed record ConsumptionDropRow(
    string EventId,
    string InputFamily,
    string InputTier,
    string InputRarity,
    string OutputItem,
    string OutputTier,
    string OutputRarity,
    int Count,
    double GoldenEggs,
    string? ClientVersion);

public sealed record RawConsumptionEvent(
    long Id,
    string SpecName,
    string SpecLevel,
    string SpecRarity,
    string Byproducts,
    double GoldenEggs,
    string? ClientVersion);

public sealed class ConsumptionReportRows(EggIncognitoDbContext db) {
    public async Task<IReadOnlyList<ConsumptionDropRow>> ReadAsync(CancellationToken ct) {
        var events = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Select(o => new RawConsumptionEvent(o.Id, o.SpecName, o.SpecLevel, o.SpecRarity, o.Byproducts, o.GoldenEggs, o.ClientVersion))
            .ToListAsync(ct);
        return Materialize(events);
    }

    public static IReadOnlyList<ConsumptionDropRow> Materialize(IReadOnlyList<RawConsumptionEvent> events) {
        var parsed = events
            .Select(e => (e.Id, e.SpecName, e.SpecLevel, e.SpecRarity, e.GoldenEggs, e.ClientVersion,
                Byproducts: ArtifactObservationQuery.ParseByproducts(e.Byproducts)))
            .ToList();

        var candidates = parsed
            .GroupBy(e => (e.SpecName, e.SpecLevel, e.SpecRarity))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(string Name, string Level, string Rarity)>)
                [.. g.SelectMany(e => e.Byproducts).Select(b => (b.Name, b.Level, b.Rarity)).Distinct()]);

        var rows = new List<ConsumptionDropRow>();
        foreach (var e in parsed) {
            var eventId = e.Id.ToString(CultureInfo.InvariantCulture);
            foreach (var (name, level, rarity) in candidates[(e.SpecName, e.SpecLevel, e.SpecRarity)]) {
                int count = e.Byproducts
                    .Where(b => b.Name == name && b.Level == level && b.Rarity == rarity)
                    .Sum(b => Math.Max(1, b.Count));
                rows.Add(new ConsumptionDropRow(eventId, e.SpecName, e.SpecLevel, e.SpecRarity,
                    name, level, rarity, count, e.GoldenEggs, e.ClientVersion));
            }
        }

        return rows;
    }
}
