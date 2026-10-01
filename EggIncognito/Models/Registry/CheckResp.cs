namespace EggIncognito.Models.Registry;

public sealed record CheckResp(bool InRegistry, bool Pending, bool KnownCombination, bool Archived = false);
