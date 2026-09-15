namespace EggIncognito.Models.Devices;

public sealed record IslandRow(int AndroidUserId, string Label, bool Provisioned, string? EggAccountId);
