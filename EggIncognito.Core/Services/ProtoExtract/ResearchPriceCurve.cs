namespace EggIncognito.Core.Services.ProtoExtract;

public static class ResearchPriceCurve {
    public const int Standard = 0;
    public const int Virtue = 1;

    public readonly record struct Point(double FarmSize, double ChickenValue);

    public sealed record Curve(double Step, IReadOnlyList<Point> Points);

    public readonly record struct Constants(double SizeBase, double StandardValueBase, double VirtueValueBase,
        double TailScale);

    public static Curve Build(int type, Constants k) {
        double step = 0.1f;
        var points = new List<Point>();
        if (type is not (Standard or Virtue)) return new Curve(step, points);
        double sizeScale = type == Standard ? 3500 : 25000;
        for (int i = 0; 50 / step > i; i++) {
            double x = step * i;
            double level = Math.FusedMultiplyAdd(Math.Min(i, 110), 0.5, 5);
            double size = Math.FusedMultiplyAdd(Math.Pow(k.SizeBase, x), sizeScale, -sizeScale);
            double value = type == Standard
                ? level * (Math.Pow(k.StandardValueBase, x) * 0.25 * 2 * StandardGrowth(x, k.TailScale))
                : Math.Pow(k.VirtueValueBase, x) * (level * (2 * Math.Pow(10, x)));
            points.Add(new Point(size, value));
        }

        return new Curve(step, points);
    }

    private static double StandardGrowth(double x, double tailScale) {
        if (!(x > 15)) return Math.Pow(10, x);
        double over = x + -15;
        double b = 10 - over;
        return Math.Pow(b < 7 ? 7 : b, over) * tailScale;
    }

    public static double ChickenValueFromEggLevel(Curve curve, double eggLevel) {
        var p = curve.Points;
        double q = eggLevel / curve.Step;
        int idx = (int)q;
        ulong last = (ulong)(p.Count - 1);
        if (last <= (ulong)(long)idx) return p[(int)last].ChickenValue;
        double frac = (float)(q - idx);
        return Math.FusedMultiplyAdd(frac, p[idx + 1].ChickenValue - p[idx].ChickenValue, p[idx].ChickenValue);
    }

    public static double FarmSize(Curve curve, double chickenValue) {
        var p = curve.Points;
        for (int k = 0; k + 1 < p.Count; k++) {
            if (!(chickenValue < p[k + 1].ChickenValue)) continue;
            double frac = (float)((chickenValue - p[k].ChickenValue) / (p[k + 1].ChickenValue - p[k].ChickenValue));
            return Math.FusedMultiplyAdd(frac, p[k + 1].FarmSize - p[k].FarmSize, p[k].FarmSize);
        }

        return p[^1].FarmSize;
    }

    public static double FarmValue(Curve curve, double farmSize) {
        var p = curve.Points;
        if (p.Count < 2) return 0;
        double acc = 0;
        for (int k = 1; k < p.Count; k++) {
            if (farmSize > p[k].FarmSize) {
                acc += (p[k].FarmSize - p[k - 1].FarmSize) * ((p[k].ChickenValue + p[k - 1].ChickenValue) * 0.5);
                continue;
            }

            double width = farmSize - p[k - 1].FarmSize;
            return acc + width * ((p[k - 1].ChickenValue + ValueAtSize(p, farmSize)) * 0.5);
        }

        return acc;
    }

    private static double ValueAtSize(IReadOnlyList<Point> p, double farmSize) {
        for (int j = 1; j < p.Count; j++) {
            if (!(farmSize < p[j].FarmSize)) continue;
            double frac = (float)((farmSize - p[j - 1].FarmSize) / (p[j].FarmSize - p[j - 1].FarmSize));
            return Math.FusedMultiplyAdd(frac, p[j].ChickenValue - p[j - 1].ChickenValue, p[j - 1].ChickenValue);
        }

        return p[^1].ChickenValue;
    }

    public static double CommonPrice(double priceStart, double priceEnd, double pricePercent, int maxLevel, int level,
        Curve curve, double costMult = 1.0) {
        double t = maxLevel >= 2 ? priceStart + (double)level / (maxLevel - 1) * (priceEnd - priceStart) : priceStart;
        return FarmValue(curve, FarmSize(curve, ChickenValueFromEggLevel(curve, t))) * (pricePercent / 100) * costMult;
    }

    public static int EpicPrice(double epicStart, double epicEnd, int maxLevel, int level, double costMult = 1.0) =>
        Math.Max(1, (int)(costMult * (float)(epicStart + (double)level / (maxLevel - 1) * (epicEnd - epicStart))));
}
