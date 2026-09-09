namespace EggIncognito.Models.Tools;

public sealed record EndpointStatusResult(
    IReadOnlyList<string> Ok,
    IReadOnlyList<string> Empty,
    IReadOnlyList<string> Missing);
