namespace EggIncognito.Models.Calendar;

public interface ICalendarSpan {
    string Key { get; }

    double Start { get; }

    double End { get; }
}
