namespace EggIncognito.Core.Services.Devices;

public sealed record DeviceIsland(
    string DeviceId,
    int UserId,
    string Label,
    bool Provisioned,
    string? EggAccountId,
    DateTimeOffset CreatedAt);
