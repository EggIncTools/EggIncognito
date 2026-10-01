namespace EggIncognito.Models.Devices;

public sealed record PixelWatchGroupRequest(IReadOnlyList<string> PointIds, int OffsetMs);
