namespace EggIncognito.Models.Devices;

public sealed record PixelWatchGroup(string Id, string Color, int OffsetMs, IReadOnlyList<string> PointIds) {
    public int Position(string pointId) {
        for (int i = 0; i < PointIds.Count; i++) {
            if (string.Equals(PointIds[i], pointId, StringComparison.Ordinal)) return i;
        }

        return -1;
    }
}
