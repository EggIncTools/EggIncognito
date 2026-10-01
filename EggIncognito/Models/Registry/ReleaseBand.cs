using EggIncognito.Services.Protos;

namespace EggIncognito.Models.Registry;

public sealed record ReleaseBand(long Key, IReadOnlyList<Release> Releases) {
    public ProtoRegistryRow Primary => Releases[0].Primary;

    public static List<ReleaseBand> Of(IReadOnlyList<Release> ordered) {
        var runs = new List<List<Release>>();
        foreach (Release release in ordered) {
            if (runs.Count > 0 && SameProto(runs[^1][0].Primary, release.Primary)) runs[^1].Add(release);
            else runs.Add([release]);
        }

        return [.. runs.Select(run => new ReleaseBand(run[0].Key, run))];
    }

    public static bool SameProto(ProtoRegistryRow a, ProtoRegistryRow b) =>
        !string.IsNullOrWhiteSpace(a.ProtoSha) && !string.IsNullOrWhiteSpace(a.ClientVersion)
        && string.Equals(a.ProtoSha, b.ProtoSha, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.ClientVersion, b.ClientVersion, StringComparison.OrdinalIgnoreCase);
}
