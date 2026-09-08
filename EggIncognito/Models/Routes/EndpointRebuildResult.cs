namespace EggIncognito.Models.Routes;

public sealed record EndpointRebuildResult(int Discovered, int New, int DriftCount, string? BinaryVersion, string? Note);
