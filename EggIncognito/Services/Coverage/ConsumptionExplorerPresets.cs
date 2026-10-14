using EggIdentity.Reports;

namespace EggIncognito.Services.Coverage;

public sealed record ConsumptionPreset(string Title, ReportDefinition Definition);

public static class ConsumptionExplorerPresets {
    public static readonly IReadOnlyList<ConsumptionPreset> All = [
        new("What does each item drop", new ReportDefinition(
            ReportCodec.CurrentVersion, "consumption-drops-by-item", "What does each item drop",
            new ReportQuery(ConsumptionReportSource.Source.Id) {
                GroupBy = [new GroupKey(ConsumptionReportSource.InputFamily.Key), new GroupKey(ConsumptionReportSource.OutputItem.Key)],
                Measures = [new MeasureRequest(ConsumptionReportSource.DropChance.Key, Aggregation.Avg)],
            },
            new ReportView(ReportCharts.Heatmap))),
        new("Where does a byproduct come from", new ReportDefinition(
            ReportCodec.CurrentVersion, "consumption-source-of-byproduct", "Where does a byproduct come from",
            new ReportQuery(ConsumptionReportSource.Source.Id) {
                GroupBy = [new GroupKey(ConsumptionReportSource.OutputItem.Key), new GroupKey(ConsumptionReportSource.InputFamily.Key)],
                Measures = [new MeasureRequest(ConsumptionReportSource.DropChance.Key, Aggregation.Avg)],
            },
            new ReportView(ReportCharts.Table))),
        new("Rarity vs tier yield", new ReportDefinition(
            ReportCodec.CurrentVersion, "consumption-rarity-vs-tier", "Rarity vs tier yield",
            new ReportQuery(ConsumptionReportSource.Source.Id) {
                GroupBy = [new GroupKey(ConsumptionReportSource.InputRarity.Key), new GroupKey(ConsumptionReportSource.InputTier.Key)],
                Measures = [new MeasureRequest(ConsumptionReportSource.Count.Key, Aggregation.Avg)],
            },
            new ReportView(ReportCharts.Heatmap))),
    ];
}
