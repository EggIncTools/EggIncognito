namespace EggIncognito.Models.Devices;

public sealed record PixelWatchState(
    IReadOnlyList<PixelWatchStatus> Points, IReadOnlyList<PixelWatchGroup> Groups, bool Tapping) {
    public static readonly PixelWatchState Empty = new([], [], false);
}
