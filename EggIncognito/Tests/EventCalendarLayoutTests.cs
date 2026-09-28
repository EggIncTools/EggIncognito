using EggIncognito.Models.Events;
using EggIncognito.Services.Events;

namespace EggIncognito.Tests;

public class EventCalendarLayoutTests {
    private static CalendarItem Item(string key, DateTimeOffset start, DateTimeOffset end) =>
        new(key, UnixSeconds.FromTime(start), UnixSeconds.FromTime(end), CalendarItemKind.Event, null, null, null, null);

    [Fact]
    public void Rows_EventStartingAsAnotherEnds_SharesItsLane() {
        var zone = TimeZoneInfo.Utc;
        var (start, end) = EventCalendarLayout.Window(
            new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero), EventCalendarZoom.Week, zone);
        var items = new[] {
            Item("research", start.AddDays(1), start.AddDays(2)),
            Item("piggy", start.AddDays(1.5), start.AddDays(3)),
            Item("prestige", start.AddDays(2), start.AddDays(3)),
        };

        var row = Assert.Single(EventCalendarLayout.Rows(items, start, end, EventCalendarZoom.Week, start, zone));

        Assert.Equal(2, row.Lanes.Count);
        Assert.Equal(["research", "prestige"], row.Lanes[0].Select(b => b.Item.Key));
    }
}
