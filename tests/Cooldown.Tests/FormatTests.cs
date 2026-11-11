using Cooldown.Core;

namespace Cooldown.Tests;

// Format.Use12h and the data files are shared by the whole app, so these classes run one at a time.
[Collection("Shared state")]
public class FormatTests
{
    static TimeSpan T(int d, int h, int m, int s) => new(d, h, m, s);

    [Fact]
    public void Long_shows_days_without_seconds() => Assert.Equal("2d 21h 05m", Format.Long(T(2, 21, 5, 42)));

    [Fact]
    public void Long_shows_seconds_in_the_last_day() => Assert.Equal("21h 05m 42s", Format.Long(T(0, 21, 5, 42)));

    [Fact]
    public void Long_drops_zero_hours() => Assert.Equal("5m 42s", Format.Long(T(0, 0, 5, 42)));

    [Fact]
    public void Negative_time_shows_as_zero()
    {
        Assert.Equal("0m 00s", Format.Long(TimeSpan.FromMinutes(-3)));
        Assert.Equal("0m", Format.Compact(TimeSpan.FromMinutes(-3)));
    }

    [Theory]
    [InlineData(2, 21, 5, "2d 21h")]
    [InlineData(0, 4, 5, "4h 05m")]
    [InlineData(0, 0, 12, "12m")]
    public void Compact(int d, int h, int m, string expected) => Assert.Equal(expected, Format.Compact(T(d, h, m, 30)));

    [Fact]
    public void Clock_in_24_hour_time()
    {
        Format.Use12h = false;
        Assert.Equal("16:05", Format.Clock(new TimeSpan(16, 5, 0)));
        Assert.Equal("00:30", Format.Clock(new TimeSpan(0, 30, 0)));
    }

    [Fact]
    public void Clock_in_12_hour_time()
    {
        Format.Use12h = true;
        try
        {
            Assert.Equal("4:05 PM", Format.Clock(new TimeSpan(16, 5, 0)));
            Assert.Equal("12:30 AM", Format.Clock(new TimeSpan(0, 30, 0)));
        }
        finally { Format.Use12h = false; }
    }
}
