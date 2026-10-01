namespace EggIncognito.Services.Predictions;

public sealed record RuleEvidence(int Observed, int Expected, double LastStart, string Summary) {
    public double Fill => Expected > 0 ? Math.Min(1, Observed / (double)Expected) : 0;
}
