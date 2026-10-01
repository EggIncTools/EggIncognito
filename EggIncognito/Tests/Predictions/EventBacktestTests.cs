using EggIncognito.Models.Events;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class EventBacktestTests {
    [Fact]
    public void Run_TemplateHistory_HitsEverySlotAndEveryLaneType() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-364), EventTemplate.End.AddDays(120));

        var result = EventBacktest.Run(rows, EventTemplate.AsOf, 28);

        Assert.Equal(EventTemplate.AsOf, result.AsOf);
        Assert.Equal(28, result.HorizonDays);
        Assert.Equal(0, result.ActualUncovered);
        Assert.All(result.Kinds, k => {
            Assert.True(k.Predicted > 0, k.Kind.ToString());
            Assert.Equal(k.Predicted, k.SlotHit);
            Assert.True(k.Top3Hit >= k.TypeHit);
        });
        foreach (var kind in new[] { EventRuleKind.WeekdayLane, EventRuleKind.Alternating, EventRuleKind.Periodic }) {
            var lane = result.Kinds.Single(k => k.Kind == kind);
            Assert.Equal(lane.Predicted, lane.TypeHit);
        }
    }

    [Fact]
    public void Run_NoHistoryInWindow_PredictsNothingAndCountsActualAsUncovered() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-500), EventTemplate.End.AddDays(-200));
        rows.AddRange(EventTemplate.Build(EventTemplate.End.AddDays(1), EventTemplate.End.AddDays(20)));

        var result = EventBacktest.Run(rows, EventTemplate.AsOf, 28);

        Assert.All(result.Kinds, k => Assert.Equal(0, k.Predicted));
        Assert.True(result.ActualUncovered > 0);
    }

    [Fact]
    public void Run_HorizonOutOfRange_ClampsToNinetyDays() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-364), EventTemplate.End.AddDays(200));

        var result = EventBacktest.Run(rows, EventTemplate.AsOf, 500);

        Assert.Equal(90, result.HorizonDays);
    }

    [Fact]
    public void Sweep_TemplateHistory_SumsEveryWindow() {
        var rows = EventTemplate.Build(EventTemplate.End.AddDays(-700), EventTemplate.End.AddDays(30));

        var sweep = EventBacktest.Sweep(rows, EventTemplate.AsOf, 3);

        Assert.Equal(3, sweep.Windows.Count);
        var pool = sweep.Totals.Single(k => k.Kind == EventRuleKind.Pool);
        Assert.Equal(sweep.Windows.Sum(w => w.Kinds.Single(k => k.Kind == EventRuleKind.Pool).Predicted), pool.Predicted);
        Assert.Equal(sweep.Windows.Sum(w => w.Kinds.Single(k => k.Kind == EventRuleKind.Pool).Top3Hit), pool.Top3Hit);
        Assert.Equal(pool.Predicted, pool.SlotHit);
        var lanes = sweep.Totals.Single(k => k.Kind == EventRuleKind.WeekdayLane);
        Assert.Equal(lanes.Predicted, lanes.TypeHit);
    }
}
