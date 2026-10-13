using Ei;
using EggSharp.GameData.Farm;

namespace EggIncognito.Core.Services.Farm;

public sealed record FarmPlacement(
    ShellDB.Types.FarmElement Element,
    ShellSpec.Types.AssetType? AssetType,
    int Index,
    Vec3 Pos,
    Vec3 RotDeg,
    float Scale,
    PlacementProvenance Provenance) {
    public string? Stem { get; init; }

    public static FarmPlacement At(ShellDB.Types.FarmElement element, ShellSpec.Types.AssetType type, int index,
        Vec3 pos, PlacementProvenance provenance) =>
        new(element, type, index, pos, Vec3.Zero, 1f, provenance);
}

public readonly record struct FarmExtents(float Lab, float Depot, float Hatchery, bool HatcheryResolved);

public sealed record FarmLayout(IReadOnlyList<FarmPlacement> Placements, FarmExtents Extents);
