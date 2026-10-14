using EggIdentity.Filters;
using EggIdentity.Reports;
using EggSharp.GameData.Artifacts;

namespace EggIncognito.Services.Coverage;

public static class ConsumptionReportSource {
    public const string EventKey = "event";

    private static readonly IReadOnlyList<FilterOption> TierOptions =
        [.. Enum.GetNames<Ei.ArtifactSpec.Types.Level>().Select(n => new FilterOption(n, n))];

    private static readonly IReadOnlyList<FilterOption> RarityOptions =
        [.. Enum.GetNames<Ei.ArtifactSpec.Types.Rarity>().Select(n => new FilterOption(n, n))];

    private static readonly IReadOnlyList<FilterOption> InputFamilyOptions =
        [.. ArtifactDefinitions.All.Where(f => f.Kind == ArtifactKind.Artifact).OrderBy(f => f.Order)
            .Select(f => new FilterOption(f.Name, f.PluralName))];

    private static readonly IReadOnlyList<FilterOption> OutputItemOptions =
        [.. ArtifactDefinitions.All.OrderBy(f => f.Order).Select(f => new FilterOption(f.Name, f.PluralName))];

    public static readonly ReportDimension InputFamily = new(new FilterFieldDef(
        "input_family", "Input item", FilterValueKind.Select, FilterOps.Equality) { Scope = "input", Options = InputFamilyOptions }) {
        Order = DimensionOrder.OptionOrder,
    };

    public static readonly ReportDimension InputTier = new(new FilterFieldDef(
        "input_tier", "Input tier", FilterValueKind.Select, FilterOps.Equality) { Scope = "input", Options = TierOptions }) {
        Order = DimensionOrder.OptionOrder,
    };

    public static readonly ReportDimension InputRarity = new(new FilterFieldDef(
        "input_rarity", "Input rarity", FilterValueKind.Select, FilterOps.Equality) { Scope = "input", Options = RarityOptions }) {
        Order = DimensionOrder.OptionOrder,
    };

    public static readonly ReportDimension OutputItem = new(new FilterFieldDef(
        "output_item", "Byproduct", FilterValueKind.Select, FilterOps.Equality) { Scope = "output", Options = OutputItemOptions }) {
        Order = DimensionOrder.OptionOrder,
    };

    public static readonly ReportDimension OutputTier = new(new FilterFieldDef(
        "output_tier", "Byproduct tier", FilterValueKind.Select, FilterOps.Equality) { Scope = "output", Options = TierOptions }) {
        Order = DimensionOrder.OptionOrder,
    };

    public static readonly ReportDimension OutputRarity = new(new FilterFieldDef(
        "output_rarity", "Byproduct rarity", FilterValueKind.Select, FilterOps.Equality) { Scope = "output", Options = RarityOptions }) {
        Order = DimensionOrder.OptionOrder,
    };

    public static readonly ReportDimension ClientVersion = new(new FilterFieldDef(
        "client_version", "Client version", FilterValueKind.Version, FilterOps.Comparison));

    public static readonly ReportMeasure Observations = new("events", "Observations") {
        Aggregations = [Aggregation.Distinct],
        Default = Aggregation.Distinct,
        DistinctBy = EventKey,
    };

    public static readonly ReportMeasure DropChance = new("drop_chance", "Drop chance") {
        Aggregations = [Aggregation.Avg],
        Default = Aggregation.Avg,
        Format = ValueFormat.Percent,
    };

    public static readonly ReportMeasure Count = new("count", "Count") {
        Aggregations = [Aggregation.Avg, Aggregation.Min, Aggregation.Max, Aggregation.Sum],
        Default = Aggregation.Avg,
    };

    public static readonly ReportMeasure CountDistribution = new("count_distribution", "Count distribution") {
        Kind = MeasureKind.Distribution,
        Aggregations = [Aggregation.Avg],
        Default = Aggregation.Avg,
    };

    public static readonly ReportMeasure GoldenEggs = new("golden_eggs", "Gold eggs") {
        Aggregations = [Aggregation.Avg, Aggregation.Min, Aggregation.Max],
        Default = Aggregation.Avg,
        DistinctBy = EventKey,
    };

    public static readonly ReportMeasure GoldenEggsDistribution = new("golden_eggs_distribution", "Gold eggs distribution") {
        Kind = MeasureKind.Distribution,
        Aggregations = [Aggregation.Avg],
        Default = Aggregation.Avg,
        DistinctBy = EventKey,
    };

    public static readonly ReportSource<ConsumptionDropRow> Source = new(
        "consumption",
        "Consumption",
        [InputFamily, InputTier, InputRarity, OutputItem, OutputTier, OutputRarity, ClientVersion],
        [Observations, DropChance, Count, CountDistribution, GoldenEggs, GoldenEggsDistribution],
        Read) {
        SampleKey = EventKey,
    };

    public static FilterSubjectValue Read(ConsumptionDropRow row, string key) => key switch {
        "input_family" => FilterSubjectValue.Of(row.InputFamily),
        "input_tier" => FilterSubjectValue.Of(row.InputTier),
        "input_rarity" => FilterSubjectValue.Of(row.InputRarity),
        "output_item" => FilterSubjectValue.Of(row.OutputItem),
        "output_tier" => FilterSubjectValue.Of(row.OutputTier),
        "output_rarity" => FilterSubjectValue.Of(row.OutputRarity),
        "client_version" => row.ClientVersion is { Length: > 0 } v ? FilterSubjectValue.Of(v) : FilterSubjectValue.Absent,
        EventKey or "events" => FilterSubjectValue.Of(row.EventId),
        "drop_chance" => FilterSubjectValue.Of(row.Count > 0 ? 1d : 0d),
        "count" or "count_distribution" => FilterSubjectValue.Of((double)row.Count),
        "golden_eggs" or "golden_eggs_distribution" => FilterSubjectValue.Of(row.GoldenEggs),
        _ => FilterSubjectValue.Absent,
    };

    public static readonly IReadOnlyList<IReportSource> Sources = [Source];

    public static InMemoryReportEngine<ConsumptionDropRow> Engine(
        Func<CancellationToken, ValueTask<IReadOnlyList<ConsumptionDropRow>>> rows, TimeProvider clock) =>
        new(Source, rows, clock);
}
