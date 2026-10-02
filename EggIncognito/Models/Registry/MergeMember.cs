namespace EggIncognito.Models.Registry;

public sealed record MergeMember(string Platform, string Build, string? AppVersion = null);
