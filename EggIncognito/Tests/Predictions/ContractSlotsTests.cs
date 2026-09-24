using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

public class ContractSlotsTests {
    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    private static double UtcSeconds(int year, int month, int day, int hour) =>
        UnixSeconds.FromTime(Utc(year, month, day, hour));

    [Fact]
    public void Next_FromTuesdayInSummer_UsesEdtNoon() {
        var slots = ContractSlots.Next(Utc(2026, 6, 16, 8), 1);

        var (time, kind) = Assert.Single(slots);
        Assert.Equal(UtcSeconds(2026, 6, 17, 16), time);
        Assert.Equal(Assert.Single(ContractSlots.KindsFor(DayOfWeek.Wednesday)), kind);
    }

    [Fact]
    public void Next_FromTuesdayInWinter_UsesEstNoon() {
        var slots = ContractSlots.Next(Utc(2026, 1, 13, 8), 1);

        var (time, kind) = Assert.Single(slots);
        Assert.Equal(UtcSeconds(2026, 1, 14, 17), time);
        Assert.Equal(Assert.Single(ContractSlots.KindsFor(DayOfWeek.Wednesday)), kind);
    }

    [Fact]
    public void Next_AcrossWeekBoundary_OrderedAndFridayPairShareTime() {
        var slots = ContractSlots.Next(Utc(2026, 6, 19, 20), 5);

        ContractSlotKind[] expected = [
            .. ContractSlots.KindsFor(DayOfWeek.Monday),
            .. ContractSlots.KindsFor(DayOfWeek.Wednesday),
            .. ContractSlots.KindsFor(DayOfWeek.Friday),
            .. ContractSlots.KindsFor(DayOfWeek.Monday)
        ];

        Assert.Equal(5, slots.Count);
        Assert.Equal(expected, slots.Select(s => s.Kind).ToList());
        Assert.Equal(UtcSeconds(2026, 6, 22, 16), slots[0].Time);
        Assert.Equal(UtcSeconds(2026, 6, 24, 16), slots[1].Time);
        Assert.Equal(UtcSeconds(2026, 6, 26, 16), slots[2].Time);
        Assert.Equal(slots[2].Time, slots[3].Time);
        Assert.Equal(UtcSeconds(2026, 6, 29, 16), slots[4].Time);
        Assert.Equal([.. slots.Select(s => s.Time).Order()], [.. slots.Select(s => s.Time)]);
    }

    [Fact]
    public void Next_AtExactSlotTime_SkipsThatSlot() {
        var slots = ContractSlots.Next(Utc(2026, 6, 19, 16), 1);

        var (time, kind) = Assert.Single(slots);
        Assert.Equal(Assert.Single(ContractSlots.KindsFor(DayOfWeek.Monday)), kind);
        Assert.Equal(UtcSeconds(2026, 6, 22, 16), time);
    }

    [Fact]
    public void Next_HorizonSplittingFridayPair_KeepsBoth() {
        var slots = ContractSlots.Next(Utc(2026, 6, 17, 20), 1);

        Assert.Equal(ContractSlots.KindsFor(DayOfWeek.Friday), [.. slots.Select(s => s.Kind)]);
        Assert.Equal(2, slots.Count);
        Assert.Equal(slots[0].Time, slots[1].Time);
    }

    [Fact]
    public void Next_HorizonRespectedWhenNoPairSplit() {
        var slots = ContractSlots.Next(Utc(2026, 6, 17, 20), 3);

        Assert.Equal(3, slots.Count);
        Assert.Equal(UtcSeconds(2026, 6, 22, 16), slots[2].Time);
    }

    [Fact]
    public void Next_NonPositiveHorizon_ReturnsEmpty() =>
        Assert.Empty(ContractSlots.Next(Utc(2026, 6, 17, 20), 0));

    [Fact]
    public void IsGridSlot_SlotTimeWithinTolerance_True() {
        Assert.True(ContractSlots.IsGridSlot(UtcSeconds(2026, 6, 19, 16), 300));
        Assert.True(ContractSlots.IsGridSlot(UtcSeconds(2026, 6, 19, 16) + 240, 300));
    }

    [Fact]
    public void IsGridSlot_OffGridWeekdayOrTime_False() {
        Assert.False(ContractSlots.IsGridSlot(UtcSeconds(2026, 6, 16, 16), 300));
        Assert.False(ContractSlots.IsGridSlot(UtcSeconds(2026, 6, 19, 17), 300));
    }
}
