using EggIdentity.Filters;
using EggIdentity.Reports;
using EggSharp.GameData.Artifacts;

namespace EggIncognito.Services.Coverage;

public static class ConsumptionReportSource {
    public const string EventKey = "event";
    private const string SourceId = "consumption";

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

    public static readonly ReportMeasure PiggyFill = new("piggy_fill", "Piggy fill") {
        Aggregations = [Aggregation.Avg, Aggregation.Min, Aggregation.Max],
        Default = Aggregation.Avg,
        DistinctBy = EventKey,
    };

    public static readonly ReportMeasure PiggyFillDistribution = new("piggy_fill_distribution", "Piggy fill distribution") {
        Kind = MeasureKind.Distribution,
        Aggregations = [Aggregation.Avg],
        Default = Aggregation.Avg,
        DistinctBy = EventKey,
    };

    public static readonly IReadOnlyList<ReportPreset> Presets = [
        new("consumption-drops-by-item", "What does each item drop",
            new ReportDefinition(
                ReportCodec.CurrentVersion, "consumption-drops-by-item", "What does each item drop",
                new ReportQuery(SourceId) {
                    GroupBy = [new GroupKey(InputFamily.Key), new GroupKey(OutputItem.Key)],
                    Measures = [new MeasureRequest(DropChance.Key, Aggregation.Avg)],
                },
                new ReportView(ReportCharts.Heatmap))),
        new("consumption-source-of-byproduct", "Where does a byproduct come from",
            new ReportDefinition(
                ReportCodec.CurrentVersion, "consumption-source-of-byproduct", "Where does a byproduct come from",
                new ReportQuery(SourceId) {
                    GroupBy = [new GroupKey(OutputItem.Key), new GroupKey(InputFamily.Key)],
                    Measures = [new MeasureRequest(DropChance.Key, Aggregation.Avg)],
                },
                new ReportView(ReportCharts.Table))),
        new("consumption-rarity-vs-tier", "Rarity vs tier yield",
            new ReportDefinition(
                ReportCodec.CurrentVersion, "consumption-rarity-vs-tier", "Rarity vs tier yield",
                new ReportQuery(SourceId) {
                    GroupBy = [new GroupKey(InputRarity.Key), new GroupKey(InputTier.Key)],
                    Measures = [new MeasureRequest(Count.Key, Aggregation.Avg)],
                },
                new ReportView(ReportCharts.Heatmap))),
    ];

    public static readonly ReportSource<ConsumptionDropRow> Source = new(
        SourceId,
        "Consumption",
        [InputFamily, InputTier, InputRarity, OutputItem, OutputTier, OutputRarity, ClientVersion],
        [Observations, DropChance, Count, CountDistribution, GoldenEggs, GoldenEggsDistribution, PiggyFill, PiggyFillDistribution],
        Read) {
        SampleKey = EventKey,
        Presets = Presets,
    };

    public static readonly SnapshotSchema Snapshot = new(
        SourceId,
        [
            new GroupKey(InputFamily.Key), new GroupKey(InputTier.Key), new GroupKey(InputRarity.Key),
            new GroupKey(OutputItem.Key), new GroupKey(OutputTier.Key), new GroupKey(OutputRarity.Key),
            new GroupKey(ClientVersion.Key),
        ],
        [Observations.Key, DropChance.Key, Count.Key, CountDistribution.Key, GoldenEggs.Key, GoldenEggsDistribution.Key,
            PiggyFill.Key, PiggyFillDistribution.Key]) {
        FanOut = [OutputItem.Key, OutputTier.Key, OutputRarity.Key],
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
        "piggy_fill" or "piggy_fill_distribution" => FilterSubjectValue.Of(row.PiggyFill),
        _ => FilterSubjectValue.Absent,
    };

    public static readonly IReadOnlyList<IReportSource> Sources = [Source];
}
