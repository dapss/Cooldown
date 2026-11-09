using System.Globalization;

namespace Cooldown.Core;

// One AI's limits: a weekly reset, and optionally a rolling session window (like Claude's 5 hours).
public class Reset
{
    public string Name = "";
    public DayOfWeek Day;
    public TimeSpan Time;
    public int SessionHours;      // rolling limit in hours; 0 = none
    public DateTime? SessionEnd;  // when the current session window resets; null = not started
    public DateTime LastNext;     // runtime only: lets the clock spot the moment a weekly reset passes

    public string SessionLabel => SessionHours + "-hour limit";

    public DateTime Next(DateTime now)
    {
        int days = ((int)Day - (int)now.DayOfWeek + 7) % 7;
        DateTime t = now.Date.AddDays(days).Add(Time);
        return t <= now ? t.AddDays(7) : t;
    }

    public string Schedule => Day.ToString()[..3] + " " + Format.Clock(Time);

    public Reset Copy() => new()
    {
        Name = Name, Day = Day, Time = Time, SessionHours = SessionHours, SessionEnd = SessionEnd, LastNext = LastNext
    };
}

public static class Format
{
    // Display only; resets.txt always stores 24-hour times.
    public static bool Use12h;

    public static string Clock(TimeSpan t) =>
        DateTime.Today.Add(t).ToString(Use12h ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);

    // Leaves out units that are zero at the front, and shows seconds only in the last day, when
    // they matter: "2d 21h 05m", "21h 05m 42s", "5m 42s".
    public static string Long(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.Days > 0 ? $"{t.Days}d {t.Hours}h {t.Minutes:00}m" : Hms(t);
    }

    public static string Hms(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s" : $"{t.Minutes}m {t.Seconds:00}s";
    }

    // Short enough for the collapsed island: "2d 21h", "4h 05m", "12m".
    public static string Compact(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{t.Days}d {t.Hours}h";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return $"{t.Minutes}m";
    }

    public static string Short(TimeSpan t) => $"{t.Days}d {t.Hours}h {t.Minutes}m";

    public static string When(DateTime d) => d.ToString("dddd, d MMM") + " at " + Clock(d.TimeOfDay);
}
