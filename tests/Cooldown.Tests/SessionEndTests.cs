using Cooldown.Core;

namespace Cooldown.Tests;

// Changing when a running 5-hour session resets, from the island.
public class SessionEndTests
{
    static readonly Reset Claude = new() { SessionHours = 5 };
    static readonly DateTime Now = new(2026, 11, 11, 10, 0, 30);

    [Fact]
    public void A_time_inside_the_window_is_kept_to_the_minute() =>
        Assert.Equal(new DateTime(2026, 11, 11, 13, 20, 0), Claude.ClampSessionEnd(new DateTime(2026, 11, 11, 13, 20, 45), Now));

    [Fact]
    public void No_later_than_a_full_session_from_now() =>
        Assert.Equal(new DateTime(2026, 11, 11, 15, 0, 0), Claude.ClampSessionEnd(new DateTime(2026, 11, 11, 18, 0, 0), Now));

    [Fact]
    public void At_least_a_minute_from_now() =>
        Assert.Equal(new DateTime(2026, 11, 11, 10, 2, 0), Claude.ClampSessionEnd(new DateTime(2026, 11, 11, 9, 0, 0), Now));

    [Fact]
    public void On_the_minute_the_next_minute_is_allowed() =>
        Assert.Equal(new DateTime(2026, 11, 11, 10, 1, 0),
            Claude.ClampSessionEnd(new DateTime(2026, 11, 11, 10, 1, 0), new DateTime(2026, 11, 11, 10, 0, 0)));

    [Fact]
    public void Past_midnight() =>
        Assert.Equal(new DateTime(2026, 11, 12, 1, 15, 0),
            Claude.ClampSessionEnd(new DateTime(2026, 11, 12, 1, 15, 0), new DateTime(2026, 11, 11, 22, 0, 0)));
}
