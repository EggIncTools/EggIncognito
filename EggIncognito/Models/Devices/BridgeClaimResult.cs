namespace EggIncognito.Models.Devices;

public sealed record BridgeClaimResult(bool Ok, DateTimeOffset ExpiresAt);
