namespace EggIncognito.Models.Devices;

public sealed record PixelWatchStatus(
    string Id, int X, int Y, string Color, string Kind, int HoldMs, int RateMs, string? GroupId,
    int Taps, DateTimeOffset? LastTapAt, string? Error);
