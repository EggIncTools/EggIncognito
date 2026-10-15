using EggIncognito.Services.Predictions;
using EggIncognito.Services.Predictions.Events;

namespace EggIncognito.Tests.Predictions;

public class EventDomainTests {
    private const double Day = 86400d;

    private static List<EventRow> TemplateRows() =>
        EventTemplate.Build(EventTemplate.End.AddDays(-364), EventTemplate.End.AddDays(120));

    public static IEnumerable<object[]> Strategies() => EventStrategy.Grid.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Strategies))]
    public void WalkForward_EveryStrategy_KeepsLaneKindsAtFullTop1(EventStrategy strategy) {
        var rows = TemplateRows();
        double to = EventTemplate.AsOf;
        double from = to - 90 * Day;
        var result = WalkForward.Run(new EventDomain(), rows, strategy, WalkForward.DefaultLagSeconds, from, to);

        foreach (string kind in new[] { "WeekdayLane", "Alternating", "Periodic" }) {
            var bucket = result.Kinds.FirstOrDefault(k => k.Kind == kind);
            if (bucket is null) continue;
            Assert.Equal(bucket.Slots, bucket.Top1);
        }
    }

    [Fact]
    public void WalkForward_DefaultStrategy_FindsWeekdayLaneAndAlternatingBuckets() {
        var rows = TemplateRows();
        double to = EventTemplate.AsOf;
        double from = to - 90 * Day;
        var result = WalkForward.Run(new EventDomain(), rows, EventStrategy.Default, WalkForward.DefaultLagSeconds, from, to);

        var lane = Assert.Single(result.Kinds, k => k.Kind == "WeekdayLane");
        var alternating = Assert.Single(result.Kinds, k => k.Kind == "Alternating");
        Assert.True(lane.Slots > 0);
        Assert.True(alternating.Slots > 0);
        Assert.Equal(lane.Slots, lane.Top1);
        Assert.Equal(alternating.Slots, alternating.Top1);
    }

    [Fact]
    public void Slots_SameDateMultipleUltraAndStandardOccurrences_ProduceDistinctSlots() {
        var rows = TemplateRows();
        double to = EventTemplate.AsOf;
        double from = to - 28 * Day;
        var domain = new EventDomain();

        var slots = domain.Slots(rows, from, to);

        Assert.Contains(slots, s => s.Kind == "Ultra");
        Assert.Contains(slots, s => s.Kind != "Ultra");
        Assert.All(slots, s => Assert.True(domain.Actual(rows, s).Count > 0));
    }
}
