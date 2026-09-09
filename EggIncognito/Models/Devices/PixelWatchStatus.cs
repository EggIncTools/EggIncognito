namespace EggIncognito.Models.Devices;

public sealed record PixelWatchStatus(int X, int Y, string Color, int Taps, DateTimeOffset? LastTapAt, string? Error);
