using System.Globalization;
using EggIdentity.Reports;
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
    double PiggyFill,
    string? ClientVersion);

public sealed record RawConsumptionEvent(
    long Id,
    string SpecName,
    string SpecLevel,
    string SpecRarity,
    string Byproducts,
    double GoldenEggs,
    double PiggyFill,
    string? ClientVersion);

public sealed class ConsumptionReportRows(EggIncognitoDbContext db) : IReportRowProvider<ConsumptionDropRow> {
    public async ValueTask<IReadOnlyList<ConsumptionDropRow>> ReadAsync(string? partition, CancellationToken ct) {
        var events = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Select(o => new RawConsumptionEvent(o.Id, o.SpecName, o.SpecLevel, o.SpecRarity, o.Byproducts, o.GoldenEggs, o.PiggyFill, o.ClientVersion))
            .ToListAsync(ct);
        return Materialize(events);
    }

    public async ValueTask<string> VersionAsync(string? partition, CancellationToken ct) {
        var maxId = await db.ArtifactConsumeObservations.AsNoTracking()
            .OrderByDescending(o => o.Id)
            .Select(o => o.Id)
            .FirstOrDefaultAsync(ct);
        return maxId.ToString(CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<ConsumptionDropRow> Materialize(IReadOnlyList<RawConsumptionEvent> events) {
        var parsed = events
            .Select(e => (e.Id, e.SpecName, e.SpecLevel, e.SpecRarity, e.GoldenEggs, e.PiggyFill, e.ClientVersion,
                Byproducts: ArtifactObservationQuery.ParseByproducts(e.Byproducts)))
            .ToList();

        return ReportFanOut.Rows(parsed,
            e => string.Join('|', e.SpecName, e.SpecLevel, e.SpecRarity),
            e => e.Byproducts,
            b => string.Join('|', b.Name, b.Level, b.Rarity),
            (e, candidate, item) => {
                var (name, level, rarity) = item is not null ? (item.Name, item.Level, item.Rarity) : SplitCandidate(candidate);
                var count = item is null ? 0 : Math.Max(1, item.Count);
                return new ConsumptionDropRow(e.Id.ToString(CultureInfo.InvariantCulture), e.SpecName, e.SpecLevel, e.SpecRarity,
                    name, level, rarity, count, e.GoldenEggs, e.PiggyFill, e.ClientVersion);
            });
    }

    private static (string Name, string Level, string Rarity) SplitCandidate(string candidate) {
        var parts = candidate.Split('|');
        return (parts[0], parts[1], parts[2]);
    }
}

public sealed class NoConsumptionReportRows : IReportRowProvider<ConsumptionDropRow> {
    public ValueTask<IReadOnlyList<ConsumptionDropRow>> ReadAsync(string? partition, CancellationToken ct) =>
        new(Array.Empty<ConsumptionDropRow>());

    public ValueTask<string> VersionAsync(string? partition, CancellationToken ct) => new("0");
}
