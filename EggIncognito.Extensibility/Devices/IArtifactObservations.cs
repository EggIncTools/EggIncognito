using EggIncognito.Models.Observations;
using Ei;

namespace EggIncognito.Services.Devices;

public interface IArtifactObservationSink {
    void Craft(string? deviceId, CraftArtifactRequest request, CraftArtifactResponse response);
    void Consume(string? deviceId, ConsumeArtifactRequest request, ConsumeArtifactResponse response, bool success);
}

public interface IArtifactObservationQuery {
    Task<IReadOnlyDictionary<(string Family, string Level, string Rarity), int>> ConsumedCountsAsync(CancellationToken ct);

    Task<IReadOnlyList<IReadOnlyList<ArtifactByproductRow>>> ConsumeByproductsAsync(
        string family, string level, string rarity, CancellationToken ct);
}
