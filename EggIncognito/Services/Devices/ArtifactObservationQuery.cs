using System.Text.Json;
using EggIncognito.Core.Services;
using EggIncognito.Data.Services;
using EggIncognito.Models.Coverage;
using EggIncognito.Models.Observations;
using EggIncognito.Services.Coverage;
using Ei;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public sealed class CoverageMapAccess(IServiceScopeFactory scopes) : ICoverageMap {
    public async Task<ConsumeCoverageMap?> MapAsync(CancellationToken ct) {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ConsumeCoverageService>().MapAsync(ct);
    }

    public async Task<string?> UpsertTargetAsync(CoverageTargetRequest request, string by, CancellationToken ct) {
        if (request.Error() is { } err) return err;
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ConsumeCoverageService>().UpsertAsync(request, by, ct);
        return null;
    }

    public async Task<bool?> DeleteTargetAsync(long id, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ConsumeCoverageService>().DeleteAsync(id, ct);
    }
}

public sealed class ArtifactObservationQuery(IServiceScopeFactory scopes) : IArtifactObservationQuery {
    public async Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumedCountsAsync(
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var rows = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .GroupBy(o => new { o.SpecName, o.SpecLevel, o.SpecRarity })
            .Select(g => new { g.Key.SpecName, g.Key.SpecLevel, g.Key.SpecRarity, Count = g.Sum(o => o.CountRequested) })
            .ToListAsync(ct);
        return rows.ToDictionary(x => (x.SpecName, x.SpecLevel, x.SpecRarity), x => x.Count);
    }

    public async Task<IReadOnlyList<IReadOnlyList<ArtifactByproductRow>>> ConsumeByproductsAsync(
        string family, string level, string rarity, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var rows = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Where(o => o.SpecName == family && o.SpecLevel == level && o.SpecRarity == rarity)
            .Select(o => o.Byproducts)
            .ToListAsync(ct);
        return [.. rows.Select(ParseByproducts)];
    }

    public async Task<IReadOnlyList<double>> ConsumeGoldenEggsAsync(string family, string level, string rarity,
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        return await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Where(o => o.SpecName == family && o.SpecLevel == level && o.SpecRarity == rarity)
            .Select(o => o.GoldenEggs)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<double>> ConsumePiggyFillAsync(string family, string level, string rarity,
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        return await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Where(o => o.SpecName == family && o.SpecLevel == level && o.SpecRarity == rarity)
            .Select(o => o.PiggyFill)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumeEventCountsAsync(
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var rows = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .GroupBy(o => new { o.SpecName, o.SpecLevel, o.SpecRarity })
            .Select(g => new { g.Key.SpecName, g.Key.SpecLevel, g.Key.SpecRarity, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(x => (x.SpecName, x.SpecLevel, x.SpecRarity), x => x.Count);
    }

    public async Task<IReadOnlyList<(string Family, string Level, string Rarity, IReadOnlyList<ArtifactByproductRow> Byproducts)>>
        ConsumesDroppingAsync(string name, CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var rows = await db.ArtifactConsumeObservations.AsNoTracking()
            .CountedConsumes()
            .Where(o => EF.Functions.JsonContains(o.Byproducts, JsonSerializer.Serialize(new[] { new { name } })))
            .Select(o => new { o.SpecName, o.SpecLevel, o.SpecRarity, o.Byproducts })
            .ToListAsync(ct);
        return [.. rows.Select(r => (r.SpecName, r.SpecLevel, r.SpecRarity, ParseByproducts(r.Byproducts)))];
    }

    public static IReadOnlyList<ArtifactByproductRow> ParseByproducts(string json) {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try {
            return JsonSerializer.Deserialize<List<ArtifactByproductRow>>(json, JsonPresets.Web) ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}

internal sealed class NoArtifactObservations : IArtifactObservationSink, IArtifactObservationQuery, ICoverageMap {
    public Task<ConsumeCoverageMap?> MapAsync(CancellationToken ct) => Task.FromResult<ConsumeCoverageMap?>(null);

    public Task<string?> UpsertTargetAsync(CoverageTargetRequest request, string by, CancellationToken ct) =>
        Task.FromResult<string?>("no database configured");

    public Task<bool?> DeleteTargetAsync(long id, CancellationToken ct) => Task.FromResult<bool?>(null);

    public void Craft(string? deviceId, CraftArtifactRequest request, CraftArtifactResponse response) {
    }

    public void Consume(string? deviceId, ConsumeArtifactRequest request, ConsumeArtifactResponse response, bool success) {
    }

    public Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumedCountsAsync(
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>>(
            new Dictionary<(string Family, string Level, string Rarity), int>());

    public Task<IReadOnlyList<IReadOnlyList<ArtifactByproductRow>>> ConsumeByproductsAsync(
        string family, string level, string rarity, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<IReadOnlyList<ArtifactByproductRow>>>([]);

    public Task<IReadOnlyList<double>> ConsumeGoldenEggsAsync(string family, string level, string rarity,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<double>>([]);

    public Task<IReadOnlyList<double>> ConsumePiggyFillAsync(string family, string level, string rarity,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<double>>([]);

    public Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumeEventCountsAsync(
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>>(
            new Dictionary<(string Family, string Level, string Rarity), int>());

    public Task<IReadOnlyList<(string Family, string Level, string Rarity, IReadOnlyList<ArtifactByproductRow> Byproducts)>>
        ConsumesDroppingAsync(string name, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(string Family, string Level, string Rarity, IReadOnlyList<ArtifactByproductRow> Byproducts)>>([]);
}
