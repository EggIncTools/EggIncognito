namespace EggIncognito.Models.Calendar;

public readonly record struct CalendarCoverage(double Start, double End, bool On) {
    public bool Covers(double start, double end) => On && start >= Start && end <= End;

    public CalendarCoverage Extend(double start, double end) =>
        !On || start > End || end < Start
            ? new CalendarCoverage(start, end, true)
            : new CalendarCoverage(Math.Min(Start, start), Math.Max(End, end), true);
}
