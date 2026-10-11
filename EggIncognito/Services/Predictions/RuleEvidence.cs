namespace EggIncognito.Services.Predictions;

public sealed record RuleEvidence(int Observed, int Expected, double LastStart, string Summary) {
    public double Fill => Expected > 0 ? Math.Min(1, Observed / (double)Expected) : 0;

    public double Confidence => Expected > 0 ? (Observed + 1d) / (Expected + 2d) : 0;
}
