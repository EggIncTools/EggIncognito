using EggIncognito.Models.Contracts;
using EggIncognito.Services.Events;

namespace EggIncognito.Services.Predictions.Contracts;

public sealed class ReleaseGrid {
    public const double WindowSeconds = 180d * 86400d;
    public const double ToleranceSeconds = 300d;

    public static readonly ContractSlotKind[] AllKinds = [
        ContractSlotKind.NewContract, ContractSlotKind.Leggacy,
        ContractSlotKind.PeLeggacy, ContractSlotKind.PeLeggacyUltra
    ];

    private static readonly (ContractSlotKind Kind, DayOfWeek Weekday)[] Rules = [
        (ContractSlotKind.NewContract, DayOfWeek.Monday),
        (ContractSlotKind.Leggacy, DayOfWeek.Wednesday),
        (ContractSlotKind.PeLeggacy, DayOfWeek.Friday),
        (ContractSlotKind.PeLeggacyUltra, DayOfWeek.Friday)
    ];

    private readonly Dictionary<DayOfWeek, List<GridSlotRule>> _byDay;

    private ReleaseGrid(IReadOnlyList<GridSlotRule> slots) {
        Slots = slots;
        _byDay = slots.GroupBy(s => s.Weekday).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Kind).ToList());
    }

    public IReadOnlyList<GridSlotRule> Slots { get; }

    public static DayOfWeek WeekdayOf(ContractSlotKind kind) => Rules.First(r => r.Kind == kind).Weekday;

    public static ReleaseGrid Fit(IReadOnlyList<ContractReleaseSample> samples, double asOf) {
        var recent = samples.Where(s => s.Start < asOf && asOf - s.Start <= WindowSeconds).ToList();
        var slots = Rules.Select(rule => {
            var mine = recent.Where(s => s.ReleaseKind == rule.Kind).ToList();
            int onGrid = mine.Count(s => OnSlot(s.Start, rule.Weekday));
            var evidence = new RuleEvidence(
                onGrid, mine.Count, mine.Count > 0 ? mine.Max(s => s.Start) : 0,
                $"{rule.Weekday} {NoonEastern.Noon:HH:mm} {NoonEastern.Zone.Id}, standing rule, {onGrid} of {mine.Count} recent releases on it");
            return new GridSlotRule(rule.Kind, rule.Weekday, NoonEastern.Noon, evidence);
        }).ToList();
        return new ReleaseGrid(slots);
    }

    public IReadOnlyList<GridSlotRule> SlotsOn(DayOfWeek day) =>
        _byDay.TryGetValue(day, out var slots) ? slots : [];

    public GridSlotRule SlotFor(ContractSlotKind kind) => Slots.First(s => s.Kind == kind);

    public IReadOnlyList<(double Time, GridSlotRule Slot)> Next(DateTimeOffset from, int horizonSlots) {
        var result = new List<(double Time, GridSlotRule Slot)>();
        if (horizonSlots <= 0) return result;

        double after = UnixSeconds.FromTime(from);
        var day = NoonEastern.LocalDate(from);
        int maxDays = 7 * Math.Clamp(horizonSlots, 1, 366) + 14;
        for (int i = 0; i < maxDays && result.Count < horizonSlots; i++, day = day.AddDays(1)) {
            foreach (var slot in SlotsOn(day.DayOfWeek)) {
                double time = NoonEastern.SlotTime(day, slot.Time);
                if (time <= after) continue;
                result.Add((time, slot));
            }
        }

        return result;
    }

    public static bool IsGridSlot(double time) {
        if (!UnixSeconds.IsValid(time)) return false;
        var day = NoonEastern.LocalDate(time);
        return Rules.Any(r => r.Weekday == day.DayOfWeek) && OnSlot(time, day.DayOfWeek);
    }

    private static bool OnSlot(double time, DayOfWeek weekday) {
        var day = NoonEastern.LocalDate(time);
        return day.DayOfWeek == weekday && Math.Abs(time - NoonEastern.SlotTime(day)) <= ToleranceSeconds;
    }
}
