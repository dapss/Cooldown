using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Cooldown.Core;
using Wpf.Ui.Appearance;

namespace Cooldown;

// Owns the resets and settings, ticks once a second, and coordinates the tray icon, the island and
// the main window.
public class AppController
{
    public ObservableCollection<ResetView> Resets { get; } = new();
    public Settings Settings { get; } = Settings.Load();
    public List<string> LoadErrors { get; } = new();
    public event Action? Ticked;

    readonly DispatcherTimer timer = new(DispatcherPriority.Normal);
    TrayIcon tray = null!;
    Notifier notifier = null!;
    IslandWindow island = null!;
    MainWindow? main;
    DateTime configStamp;

    // Undo for Start: which reset, what its session end was before, and until when to offer it.
    ResetView? undoFor;
    DateTime? undoPrevious;
    DateTime undoUntil;

    public void Start()
    {
        Format.Use12h = Settings.Use12h;
        Startup.RefreshPath();
        Load();
        tray = new TrayIcon(this);
        notifier = new Notifier(this, tray.Notify);
        island = new IslandWindow(this);
        ApplyMode();
        timer.Tick += (_, _) => Tick();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Start();
        Tick();
        if (Store.FirstRun) OpenMain(Resets.FirstOrDefault(), firstRun: true);
        TrimLater();
    }

    void Load()
    {
        LoadErrors.Clear();
        List<Reset> list = Store.LoadResets(LoadErrors);
        configStamp = File.GetLastWriteTimeUtc(Store.ResetsPath);
        Resets.Clear();
        foreach (Reset r in list) Resets.Add(new ResetView(r, this));
    }

    // ---- editing ----

    public bool NameTaken(string name, ResetView except) =>
        Resets.Any(v => v != except && string.Equals(Store.CleanName(v.Model.Name), Store.CleanName(name), StringComparison.OrdinalIgnoreCase));

    public ResetView Add()
    {
        string name = "New AI";
        for (int i = 2; Resets.Any(v => string.Equals(v.Model.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = "New AI " + i;
        var view = new ResetView(new Reset { Name = name, Day = DayOfWeek.Monday, Time = new TimeSpan(9, 0, 0) }, this);
        Resets.Add(view);
        Save();
        return view;
    }

    public void Remove(ResetView view)
    {
        Resets.Remove(view);
        Save();
    }

    // For a changed reset day or time. The clock spots a reset by the next one moving later, and so
    // does moving the schedule later: forget what it remembered, so the edit isn't announced as a reset.
    public void ScheduleChanged(ResetView view)
    {
        view.Model.LastNext = default;
        view.Model.WeekLeftBefore = null;
        Save();
    }

    // Writes both files; sessions first, so a renamed reset keeps its running session.
    public void Save()
    {
        Store.SaveSessions(Resets.Select(v => v.Model));
        try
        {
            Store.SaveResets(Resets.Select(v => v.Model));
            configStamp = File.GetLastWriteTimeUtc(Store.ResetsPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // resets.txt is locked, by an editor or a sync tool: the change stays in memory, and the
            // next save writes it.
            Store.Log("Saving resets.txt", e);
        }
        Tick();
    }

    // ---- sessions ----

    public void StartSession(ResetView view)
    {
        undoFor = view;
        undoPrevious = view.Model.SessionEnd;
        undoUntil = DateTime.Now.AddSeconds(8);
        SetSessionEnd(view, DateTime.Now.AddHours(view.Model.SessionHours));
    }

    public bool CanUndo(ResetView view) => undoFor == view && DateTime.Now < undoUntil;

    public void UndoStart(ResetView view)
    {
        if (!CanUndo(view)) return;
        undoFor = null;
        SetSessionEnd(view, undoPrevious);
    }

    public void SetSessionEnd(ResetView view, DateTime? end)
    {
        view.Model.SessionEnd = end;
        Store.SaveSessions(Resets.Select(v => v.Model));
        Tick();
    }

    // ---- clock ----

    void Tick()
    {
        try { if (File.GetLastWriteTimeUtc(Store.ResetsPath) != configStamp) Load(); }
        catch (Exception) { }  // file mid-save; try again next tick

        DateTime now = DateTime.Now;
        bool sessionsChanged = false;
        foreach (ResetView v in Resets)
        {
            Reset r = v.Model;
            DateTime next = r.Next(now);
            if (r.LastNext != DateTime.MinValue && next > r.LastNext)
                Notify(v, Possessive(r.Name) + " weekly limit has reset",
                    "Your full weekly usage is back. Next reset: " + Format.When(next) + ".");
            r.LastNext = next;

            if (r.SessionEnd.HasValue && now >= r.SessionEnd.Value)
            {
                // Skip stale news, such as a window that ended while the PC was off overnight.
                if (now - r.SessionEnd.Value < TimeSpan.FromHours(1))
                    Notify(v, Possessive(r.Name) + " " + r.SessionLabel + " has reset",
                        "Start a new session when you start chatting.", offerStart: true);
                r.SessionEnd = null;
                sessionsChanged = true;
            }
            Remind(v, now, next);
            v.Refresh(now);
        }
        if (sessionsChanged) Store.SaveSessions(Resets.Select(v => v.Model));
        tray.Update();
        island.UpdateTimes();
        Ticked?.Invoke();

        // Fire just after the next whole second so the display never skips.
        timer.Interval = TimeSpan.FromMilliseconds(1000 - DateTime.Now.Millisecond + 5);
    }

    void Notify(ResetView about, string title, string text, bool offerStart = false)
    {
        if (Settings.Notify) notifier.Show(title, text, about, offerStart);
    }

    // Reminders before a reset, if turned on in Settings. Each fires once, when the time left crosses
    // the chosen lead time, so restarting Cooldown inside that time doesn't repeat it.
    void Remind(ResetView v, DateTime now, DateTime next)
    {
        Reset r = v.Model;
        TimeSpan weekLeft = next - now;
        int lead = Settings.RemindWeekMinutes;
        if (lead > 0 && r.WeekLeftBefore is { } wb && wb > TimeSpan.FromMinutes(lead) && weekLeft <= TimeSpan.FromMinutes(lead))
            Notify(v, Possessive(r.Name) + " weekly limit resets in " + Lead(lead),
                "It resets " + Format.When(next) + ". Anything you haven't used by then doesn't carry over.");
        r.WeekLeftBefore = weekLeft;

        TimeSpan? sessionLeft = r.SessionEnd.HasValue ? r.SessionEnd.Value - now : null;
        lead = Settings.RemindSessionMinutes;
        if (lead > 0 && sessionLeft is { } sl && r.SessionLeftBefore is { } sb
            && sb > TimeSpan.FromMinutes(lead) && sl <= TimeSpan.FromMinutes(lead) && sl > TimeSpan.Zero)
            Notify(v, Possessive(r.Name) + " " + r.SessionLabel + " ends in " + Lead(lead),
                "It resets at " + Format.Clock(r.SessionEnd!.Value.TimeOfDay) + ". Anything left in this window doesn't carry over.");
        r.SessionLeftBefore = sessionLeft;
    }

    static string Lead(int minutes) => minutes switch
    {
        1440 => "1 day",
        60 => "1 hour",
        _ when minutes % 60 == 0 => minutes / 60 + " hours",
        _ => minutes + " minutes",
    };

    static string Possessive(string name) => name.EndsWith('s') ? name + "'" : name + "'s";

    // From Settings: shows what a notification looks like, and checks that Windows lets them through.
    public void SendTestNotification() =>
        notifier.Show("Notifications are on", "This is how Cooldown tells you when a limit resets.", Resets.FirstOrDefault());

#if DEBUG
    public void SimulateToast(string arguments) => notifier.Simulate(arguments);
#endif

    public void SetReminders(int weekMinutes, int sessionMinutes)
    {
        Settings.RemindWeekMinutes = weekMinutes;
        Settings.RemindSessionMinutes = sessionMinutes;
        Settings.Save();
    }

    // ---- settings ----

    public void SetMode(DisplayMode mode)
    {
        if (mode == Settings.Mode) return;
        Settings.Mode = mode;
        Settings.Save();
        ApplyMode();
    }

    void ApplyMode()
    {
        if (Settings.Mode == DisplayMode.Island) island.ShowFloating(Settings.IslandPosition);
        else if (Settings.Mode == DisplayMode.Taskbar) island.ShowDocked(Settings.TaskbarSpot);
        else island.Hide();
    }

    public void SetUse12h(bool on)
    {
        Settings.Use12h = Format.Use12h = on;
        Settings.Save();
        Tick();
    }

    public void SaveWindow(Rect bounds, bool maximized)
    {
        Settings.WindowBounds = bounds;
        Settings.WindowMaximized = maximized;
        Settings.Save();
    }

    public void SetHoverOpen(bool on)
    {
        Settings.HoverOpen = on;
        Settings.Save();
    }

    public void SetNotify(bool on)
    {
        Settings.Notify = on;
        Settings.Save();
    }

    public void IslandMoved(Point pillTopCenter)
    {
        Settings.IslandPosition = pillTopCenter;
        Settings.Save();
    }

    public void IslandDocked(int fromRight)
    {
        Settings.TaskbarSpot = fromRight;
        Settings.Save();
    }

    // ---- windows ----

    public void ShowMenu() => tray.ShowMenu();
    public bool MenuOpen => tray.MenuOpen;

    public void OpenMain(ResetView? select = null, bool settings = false, bool firstRun = false)
    {
        if (main == null)
        {
            ApplySystemTheme();
            main = new MainWindow(this);
            main.Closed += (_, _) => { main = null; TrimLater(); };
            main.Show();
        }
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
        Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(main).Handle);
        if (settings) main.ShowSettings();
        else if (select != null) main.Select(select);
        if (firstRun) main.ShowWelcome();
    }

    // A few seconds after starting, and after the window closes, hand memory Cooldown isn't using back
    // to Windows. Most of it was only needed to start up or to show the window; anything needed
    // again is simply read back in. This mainly lowers the number Task Manager shows for Cooldown.
    public static void TrimLater()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Native.SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        };
        t.Start();
    }

    public static void ApplySystemTheme()
    {
        App.EnsureFluentResources();
        ApplicationTheme theme = ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Light or SystemTheme.Flow or SystemTheme.Sunrise => ApplicationTheme.Light,
            _ => ApplicationTheme.Dark,
        };
        if (!brandHooked)
        {
            // Keep the brand color when Windows switches between light and dark mode.
            brandHooked = true;
            ApplicationThemeManager.Changed += (changed, _) => ApplyBrandAccent(changed);
        }
        ApplicationThemeManager.Apply(theme, Wpf.Ui.Controls.WindowBackdropType.Mica, updateAccent: false);
        ApplyBrandAccent(theme);
    }

    static bool brandHooked;

    // Cooldown's own mint and teal, from its icon, instead of the Windows accent color. Lighter shades
    // on dark backgrounds and deeper ones on light, so text on accent buttons stays readable.
    static void ApplyBrandAccent(ApplicationTheme theme)
    {
        static System.Windows.Media.Color C(uint rgb) =>
            System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (theme == ApplicationTheme.Light)
            ApplicationAccentColorManager.Apply(C(0x1FA588), C(0x178C73), C(0x10735E), C(0x0A5A49));
        else
            ApplicationAccentColorManager.Apply(C(0x3FBF9C), C(0x5FD3B0), C(0x7FE0C2), C(0xA3ECD6));
    }

    public void Exit()
    {
        timer.Stop();
        tray.Dispose();
        main?.Close();
        island.Close();
        Application.Current.Shutdown();
    }
}
