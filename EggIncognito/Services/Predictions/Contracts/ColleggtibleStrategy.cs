namespace EggIncognito.Services.Predictions.Contracts;

public enum ColleggtibleGrouping { PerContract, EggRotation }

public sealed record ColleggtibleStrategy(ColleggtibleGrouping Grouping, bool MergePeAndStandard) : IStrategy {
    public string Label => $"grouping={Grouping} merge={MergePeAndStandard}";

    public static readonly ColleggtibleStrategy Default = new(ColleggtibleGrouping.PerContract, false);

    public static readonly IReadOnlyList<ColleggtibleStrategy> Grid = Build();

    private static IReadOnlyList<ColleggtibleStrategy> Build() {
        ColleggtibleGrouping[] groupings = [ColleggtibleGrouping.PerContract, ColleggtibleGrouping.EggRotation];
        bool[] merges = [false, true];

        var all = new List<ColleggtibleStrategy>();
        foreach (var grouping in groupings)
            foreach (var merge in merges)
                all.Add(new ColleggtibleStrategy(grouping, merge));

        return [.. all.OrderBy(s => s == Default ? 0 : 1)];
    }
}
