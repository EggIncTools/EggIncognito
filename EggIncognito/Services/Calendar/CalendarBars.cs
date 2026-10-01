using EggIncognito.Models.Calendar;

namespace EggIncognito.Services.Calendar;

public static class CalendarBars {
    public static string Css<T>(CalendarBar<T> bar) where T : ICalendarSpan {
        var css = bar.Active ? "evcal-bar evcal-bar-active" : bar.Past ? "evcal-bar evcal-bar-past" : "evcal-bar";
        if (bar.ContinuesLeft) css += " evcal-bar-cut-l";
        if (bar.ContinuesRight) css += " evcal-bar-cut-r";
        return css;
    }

    public static string PositionStyle<T>(CalendarBar<T> bar) where T : ICalendarSpan =>
        FormattableString.Invariant($"--bar-start:{bar.LeftPercent:0.###};--bar-size:{bar.WidthPercent:0.###};");
}
