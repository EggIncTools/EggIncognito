namespace EggIncognito.Models.Contracts;

public sealed record ContractCandidate(string ContractId, string Name, double LastReleased, int Releases) {
    public double Probability { get; init; } = 1;
}
