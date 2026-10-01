namespace EggIncognito.Models.Devices;

public sealed record PixelWatchRequest(int X, int Y, string? Kind = null, int? HoldMs = null, int? RateMs = null);
