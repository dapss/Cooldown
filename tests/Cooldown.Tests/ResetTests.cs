using Cooldown.Core;

namespace Cooldown.Tests;

public class ResetTests
{
    // Wednesday 11 November 2026, 10:00.
    static readonly DateTime Now = new(2026, 11, 11, 10, 0, 0);

    static Reset At(DayOfWeek day, int hour, int minute = 0) => new() { Day = day, Time = new TimeSpan(hour, minute, 0) };

    [Fact]
    public void Later_today_is_today() =>
        Assert.Equal(new DateTime(2026, 11, 11, 16, 30, 0), At(DayOfWeek.Wednesday, 16, 30).Next(Now));

    [Fact]
    public void Earlier_today_is_next_week() =>
        Assert.Equal(new DateTime(2026, 11, 18, 9, 0, 0), At(DayOfWeek.Wednesday, 9).Next(Now));

    [Fact]
    public void Exactly_now_is_next_week() =>
        Assert.Equal(new DateTime(2026, 11, 18, 10, 0, 0), At(DayOfWeek.Wednesday, 10).Next(Now));

    [Fact]
    public void Another_day_this_week() =>
        Assert.Equal(new DateTime(2026, 11, 13, 12, 0, 0), At(DayOfWeek.Friday, 12).Next(Now));

    [Fact]
    public void A_day_already_past_wraps_to_next_week() =>
        Assert.Equal(new DateTime(2026, 11, 17, 12, 0, 0), At(DayOfWeek.Tuesday, 12).Next(Now));

    [Fact]
    public void Next_is_never_more_than_a_week_away()
    {
        for (int day = 0; day < 7; day++)
            for (int hour = 0; hour < 24; hour++)
            {
                TimeSpan left = At((DayOfWeek)day, hour).Next(Now) - Now;
                Assert.InRange(left, TimeSpan.FromTicks(1), TimeSpan.FromDays(7));
            }
    }
}
