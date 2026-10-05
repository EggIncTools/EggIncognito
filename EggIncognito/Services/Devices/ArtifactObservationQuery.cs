using System.Text.Json;
using EggIncognito.Data.Services;
using EggIncognito.Models.Observations;
using Ei;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Services.Devices;

public sealed class ArtifactObservationQuery(IServiceScopeFactory scopes) : IArtifactObservationQuery {
    private const string ConsumeAction = "consume";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumedCountsAsync(
        CancellationToken ct) {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EggIncognitoDbContext>();
        var rows = await db.ArtifactConsumeObservations.AsNoTracking()
            .Where(o => o.Action == ConsumeAction && o.Success)
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
            .Where(o => o.Action == ConsumeAction && o.Success
                                                  && o.SpecName == family && o.SpecLevel == level && o.SpecRarity == rarity)
            .Select(o => o.Byproducts)
            .ToListAsync(ct);
        return [.. rows.Select(Parse)];
    }

    private static IReadOnlyList<ArtifactByproductRow> Parse(string json) {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try {
            return JsonSerializer.Deserialize<List<ArtifactByproductRow>>(json, Json) ?? [];
        } catch (JsonException) {
            return [];
        }
    }
}

internal sealed class NoArtifactObservations : IArtifactObservationSink, IArtifactObservationQuery {
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
}
