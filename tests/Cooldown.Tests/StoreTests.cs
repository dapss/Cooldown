using Cooldown.Core;

namespace Cooldown.Tests;

// Store keeps its files next to the running code: here, the test output folder, never a real
// Cooldown folder. Each test starts from no files.
[Collection("Shared state")]
public class StoreTests
{
    static string P(string name) => Path.Combine(Store.Dir, name);
    static string[] Temps() => Directory.GetFiles(Store.Dir, "*.tmp");

    public StoreTests()
    {
        foreach (string f in new[] { "resets.txt", "sessions.txt", "banked.txt", "settings.txt", "cooldown.log" }) File.Delete(P(f));
        foreach (string f in Temps()) File.Delete(f);
    }

    static Reset Weekly(string name, int sessionHours = 0) =>
        new() { Name = name, Day = DayOfWeek.Tuesday, Time = new TimeSpan(12, 0, 0), SessionHours = sessionHours };

    [Fact]
    public void Data_folder_is_the_test_folder() =>
        Assert.Equal(AppContext.BaseDirectory.TrimEnd('\\'), Store.Dir.TrimEnd('\\'), ignoreCase: true);

    [Fact]
    public void Missing_file_is_a_first_run_with_the_example()
    {
        var errors = new List<string>();
        List<Reset> list = Store.LoadResets(errors);
        Assert.True(Store.FirstRun);
        Reset only = Assert.Single(list);
        Assert.Equal("Claude", only.Name);
        Assert.Equal(5, only.SessionHours);
        Assert.Empty(errors);
    }

    // 2.1.1: an empty file used to be replaced with the example, losing the list if a save was cut short.
    [Fact]
    public void Empty_file_is_left_alone()
    {
        File.WriteAllText(P("resets.txt"), "");
        Assert.Empty(Store.LoadResets(new List<string>()));
        Assert.Equal(0, new FileInfo(P("resets.txt")).Length);
    }

    [Fact]
    public void Reads_the_ways_a_line_can_be_written()
    {
        File.WriteAllLines(P("resets.txt"), new[]
        {
            "# a comment",
            "",
            "Claude | Tuesday | 12:00 | 5h",
            "GPT | wed | 4pm",
            "Gemini|Fri|9:30 AM|3",
        });
        var errors = new List<string>();
        List<Reset> list = Store.LoadResets(errors);
        Assert.Empty(errors);
        Assert.Equal(3, list.Count);
        Assert.Equal((DayOfWeek.Tuesday, new TimeSpan(12, 0, 0), 5), (list[0].Day, list[0].Time, list[0].SessionHours));
        Assert.Equal((DayOfWeek.Wednesday, new TimeSpan(16, 0, 0), 0), (list[1].Day, list[1].Time, list[1].SessionHours));
        Assert.Equal((DayOfWeek.Friday, new TimeSpan(9, 30, 0), 3), (list[2].Day, list[2].Time, list[2].SessionHours));
    }

    [Theory]
    [InlineData("Nope | Someday | 12:00")]
    [InlineData("No time | Mon")]
    [InlineData("Bad time | Mon | 25:00")]
    [InlineData("Bad hours | Mon | 12:00 | 99h")]
    [InlineData(" | Mon | 12:00")]
    [InlineData("Too | many | Mon | 12:00 | 5h")]
    public void A_line_it_cannot_read_is_reported_and_skipped(string line)
    {
        File.WriteAllLines(P("resets.txt"), new[] { "Claude | Tuesday | 12:00", line });
        var errors = new List<string>();
        List<Reset> list = Store.LoadResets(errors);
        Assert.Single(list);
        Assert.Equal(line.Trim(), Assert.Single(errors));
    }

    [Fact]
    public void Save_and_load_give_back_the_same_resets()
    {
        Store.SaveResets(new[]
        {
            Weekly("Claude", sessionHours: 5),
            new Reset { Name = "GPT", Day = DayOfWeek.Sunday, Time = new TimeSpan(0, 5, 0) },
        });
        var errors = new List<string>();
        List<Reset> list = Store.LoadResets(errors);
        Assert.Empty(errors);
        Assert.Equal(new[] { "Claude", "GPT" }, list.Select(r => r.Name));
        Assert.Equal(5, list[0].SessionHours);
        Assert.Equal((DayOfWeek.Sunday, new TimeSpan(0, 5, 0), 0), (list[1].Day, list[1].Time, list[1].SessionHours));
    }

    [Theory]
    [InlineData("#1 Claude|Pro", "1 Claude/Pro")]
    [InlineData("  ## x ", "x")]
    [InlineData("Claude #2", "Claude #2")]
    [InlineData("#", "Untitled")]
    [InlineData("   ", "Untitled")]
    public void CleanName(string typed, string stored) => Assert.Equal(stored, Store.CleanName(typed));

    // 2.1.1: a name starting with # was saved as a comment line and gone on the next load.
    [Fact]
    public void A_name_starting_with_a_hash_survives_a_reload()
    {
        Store.SaveResets(new[] { Weekly("#1 Claude") });
        var errors = new List<string>();
        Assert.Equal("1 Claude", Assert.Single(Store.LoadResets(errors)).Name);
        Assert.Empty(errors);
    }

    [Fact]
    public void A_running_session_is_kept_across_a_reload()
    {
        var end = new DateTime(2030, 1, 2, 3, 4, 5);
        Reset claude = Weekly("Claude", sessionHours: 5), gpt = Weekly("GPT");
        claude.SessionEnd = end;
        gpt.SessionEnd = end;  // no session limit, so nothing to keep
        Store.SaveSessions(new[] { claude, gpt });
        Store.SaveResets(new[] { claude, gpt });
        List<Reset> list = Store.LoadResets(new List<string>());
        Assert.Equal(end, list[0].SessionEnd);
        Assert.Null(list[1].SessionEnd);
    }

    [Fact]
    public void Banked_resets_are_kept_soonest_first()
    {
        Reset r = Weekly("Claude");
        r.AddBanked(new DateTime(2030, 3, 1, 12, 0, 0));
        r.AddBanked(new DateTime(2030, 1, 1, 12, 0, 0));
        r.AddBanked(new DateTime(2030, 2, 1, 12, 0, 0));
        Assert.Equal(new[] { 1, 2, 3 }, r.Banked.Select(b => b.Expires.Month));
    }

    [Fact]
    public void Banked_resets_are_kept_across_a_reload()
    {
        Reset claude = Weekly("Claude"), gpt = Weekly("GPT");
        claude.AddBanked(new DateTime(2030, 2, 1, 12, 0, 0));
        claude.AddBanked(new DateTime(2030, 1, 1, 9, 30, 0), reminded: true);
        gpt.AddBanked(new DateTime(2030, 5, 5, 0, 0, 0));
        Store.SaveBanked(new[] { claude, gpt });
        Store.SaveResets(new[] { claude, gpt });

        List<Reset> list = Store.LoadResets(new List<string>());
        Assert.Equal(new[] { new DateTime(2030, 1, 1, 9, 30, 0), new DateTime(2030, 2, 1, 12, 0, 0) }, list[0].Banked.Select(b => b.Expires));
        Assert.Equal(new[] { true, false }, list[0].Banked.Select(b => b.Reminded));
        Assert.Equal(new DateTime(2030, 5, 5, 0, 0, 0), Assert.Single(list[1].Banked).Expires);
    }

    [Fact]
    public void Banked_resets_follow_a_rename_and_go_with_a_removed_AI()
    {
        Reset claude = Weekly("Claude"), gpt = Weekly("GPT");
        claude.AddBanked(new DateTime(2030, 1, 1, 12, 0, 0));
        gpt.AddBanked(new DateTime(2030, 5, 5, 12, 0, 0));
        claude.Name = "Claude Max";
        Store.SaveBanked(new[] { claude });  // GPT removed
        Store.SaveResets(new[] { claude });

        Reset only = Assert.Single(Store.LoadResets(new List<string>()));
        Assert.Equal("Claude Max", only.Name);
        Assert.Single(only.Banked);
        Assert.DoesNotContain("GPT", File.ReadAllText(P("banked.txt")));
    }

    [Fact]
    public void Lines_in_banked_txt_it_cannot_read_are_skipped()
    {
        File.WriteAllLines(P("resets.txt"), new[] { "Claude | Tuesday | 12:00" });
        File.WriteAllLines(P("banked.txt"), new[]
        {
            "Claude | 2030-01-01 12:00:00",
            "Claude | not a date",
            "Nobody | 2030-01-01 12:00:00",
            "Claude",
            "",
        });
        Reset only = Assert.Single(Store.LoadResets(new List<string>()));
        Assert.Equal(new DateTime(2030, 1, 1, 12, 0, 0), Assert.Single(only.Banked).Expires);
    }

    [Fact]
    public void Banked_reminder_setting_defaults_to_a_day_and_is_saved()
    {
        Assert.Equal(1440, Settings.Load().RemindBankedMinutes);
        new Settings { RemindBankedMinutes = 4320 }.Save();
        Assert.Equal(4320, Settings.Load().RemindBankedMinutes);
    }

    // 2.1.1: saves go to a temporary file that replaces the real one.
    [Fact]
    public void Saving_leaves_no_temporary_files()
    {
        Store.SaveResets(new[] { Weekly("Claude", 5) });
        Store.SaveSessions(new[] { Weekly("Claude", 5) });
        new Settings().Save();
        Assert.Empty(Temps());
    }

    [Fact]
    public void A_locked_file_fails_the_save_and_keeps_the_old_content()
    {
        Store.SaveResets(new[] { Weekly("Claude") });
        string before = File.ReadAllText(P("resets.txt"));
        using (new FileStream(P("resets.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Exception e = Assert.ThrowsAny<Exception>(() => Store.SaveResets(new[] { Weekly("Other") }));
            Assert.True(e is IOException or UnauthorizedAccessException, e.GetType().Name);  // what AppController.Save catches
        }
        Assert.Equal(before, File.ReadAllText(P("resets.txt")));
    }

    [Fact]
    public void Log_writes_the_error_next_to_the_data()
    {
        Store.Log("Saving resets.txt", new IOException("in use"));
        string log = File.ReadAllText(P("cooldown.log"));
        Assert.Contains("Saving resets.txt", log);
        Assert.Contains("in use", log);
    }

    [Fact]
    public void Settings_save_and_load_give_back_the_same_settings()
    {
        new Settings
        {
            Mode = DisplayMode.Island, Notify = false, Use12h = true, HoverOpen = true,
            RemindWeekMinutes = 60, RemindSessionMinutes = 15, TaskbarSpot = 397,
            IslandPosition = new System.Windows.Point(1434, 1041),
            WindowBounds = new System.Windows.Rect(510, 196, 900, 640), WindowMaximized = true,
        }.Save();
        Settings s = Settings.Load();
        Assert.Equal(DisplayMode.Island, s.Mode);
        Assert.False(s.Notify);
        Assert.True(s.Use12h);
        Assert.True(s.HoverOpen);
        Assert.Equal((60, 15, 397), (s.RemindWeekMinutes, s.RemindSessionMinutes, s.TaskbarSpot));
        Assert.Equal(new System.Windows.Point(1434, 1041), s.IslandPosition);
        Assert.Equal(new System.Windows.Rect(510, 196, 900, 640), s.WindowBounds);
        Assert.True(s.WindowMaximized);
    }

    [Fact]
    public void Missing_settings_file_gives_the_defaults()
    {
        Settings s = Settings.Load();
        Assert.Equal(DisplayMode.Taskbar, s.Mode);
        Assert.True(s.Notify);
        Assert.Equal(-1, s.TaskbarSpot);
    }
}
