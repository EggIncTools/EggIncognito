namespace EggIncognito.Models.Registry;

public sealed record CheckResp(bool InRegistry, bool Pending, bool KnownCombination, bool Conflict, StoredMeta? Stored);

public sealed record StoredMeta(string Platform, string? AppVersion, string? Build, string? ClientVersion);
