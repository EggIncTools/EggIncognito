namespace EggIncognito.Models.Devices;

public sealed record PixelWatchState(
    IReadOnlyList<PixelWatchStatus> Points, IReadOnlyList<PixelWatchGroup> Groups, bool Tapping, bool Paused) {
    public static readonly PixelWatchState Empty = new([], [], false, false);
}
