namespace EggIncognito.Core.Services.Devices;

public sealed record DeviceIsland(
    string DeviceId,
    int AndroidUserId,
    string Label,
    bool Provisioned,
    string? EggAccountId,
    DateTimeOffset CreatedAt);
