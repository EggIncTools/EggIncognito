namespace EggIncognito.Models.Devices;

public sealed record IslandRow(int UserId, string Label, bool Provisioned, string? EggAccountId);
