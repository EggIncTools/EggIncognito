namespace EggIncognito.Services.Predictions;

public sealed record RuleEvidence(int Observed, int Expected, double LastStart, string Summary, bool Holding = false) {
    public double Fill => Expected > 0 ? Math.Min(1, Observed / (double)Expected) : 0;

    public double Confidence => Holding ? 1 : Expected > 0 ? (Observed + 1d) / (Expected + 2d) : 0;
}
