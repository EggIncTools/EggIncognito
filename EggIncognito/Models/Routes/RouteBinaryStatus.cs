using EggIncognito.Core.Services;

namespace EggIncognito.Models.Routes;

public sealed record RouteBinaryStatus(
    DateTimeOffset? LastRefresh,
    string? BinaryVersion,
    int Discovered,
    int NewCount,
    int DriftCount,
    List<RouteBinaryRow> Rows,
    List<RouteDriftRow> Drift);
