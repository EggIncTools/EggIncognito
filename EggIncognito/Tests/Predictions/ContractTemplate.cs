using EggIncognito.Services.Events;
using EggIncognito.Services.Predictions;

namespace EggIncognito.Tests.Predictions;

internal static class ContractTemplate {
    public const int LeggacyLengthDays = 5;
    public const int PeLengthDays = 7;
    public const int LeggacySeeds = 12;
    public const int PePoolSize = 16;
    public static readonly DateOnly LastMonday = new(2026, 8, 31);
    public static DateTimeOffset AsOf => Day(LastMonday.AddDays(2)).AddHours(1);

    public static DateTimeOffset Day(DateOnly day) => UnixSeconds.ToTime(NoonEastern.SlotTime(day));

    public static List<ContractReleaseSample> Build(int weeks, DateTimeOffset? endAt = null) {
        var lastMonday = endAt is { } end ? NoonEastern.LocalDate(end).AddDays(-(int)NoonEastern.LocalDate(end).DayOfWeek + 1) : LastMonday;
        if (lastMonday > NoonEastern.LocalDate(endAt ?? AsOf)) lastMonday = lastMonday.AddDays(-7);
        var firstMonday = lastMonday.AddDays(-7 * (weeks - 1));
        var rows = new List<ContractReleaseSample>();
        var pool = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int i = 0; i < LeggacySeeds; i++) {
            string id = $"seed-{i}";
            double start = NoonEastern.SlotTime(firstMonday.AddDays(-7 * (LeggacySeeds - i)));
            pool[id] = start;
            rows.Add(new ContractReleaseSample(id, id, start, PeLengthDays * 86400d, false, 0, false));
        }

        for (int cycle = 0; cycle < weeks; cycle++) {
            var monday = firstMonday.AddDays(7 * cycle);
            string fresh = $"new-{cycle}";
            double mondayNoon = NoonEastern.SlotTime(monday);
            pool[fresh] = mondayNoon;
            rows.Add(new ContractReleaseSample(fresh, $"New {cycle}", mondayNoon, PeLengthDays * 86400d, false, 0, false));
            string oldest = pool.OrderBy(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
            double wednesdayNoon = NoonEastern.SlotTime(monday.AddDays(2));
            pool[oldest] = wednesdayNoon;
            rows.Add(new ContractReleaseSample(oldest, oldest, wednesdayNoon, LeggacyLengthDays * 86400d, true, 0, false));
            rows.Add(Pe(cycle % PePoolSize, monday, false));
            rows.Add(Pe((cycle + PePoolSize / 2) % PePoolSize, monday, true));
        }

        return [.. rows.OrderBy(r => r.Start)];
    }

    private static ContractReleaseSample Pe(int ix, DateOnly monday, bool ultra) => new(
        $"pe-{ix}", $"PE {ix}", NoonEastern.SlotTime(monday.AddDays(4)), PeLengthDays * 86400d, true, 1, ultra);
}
