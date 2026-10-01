namespace EggIncognito.Models.Calendar;

public sealed record CalendarLoad(string Note, bool Covered) {
    public static readonly CalendarLoad Ok = new("", true);

    public static CalendarLoad Failed(string note) => new(note, false);
}
