using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Cooldown.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cooldown;

// Add, edit and remove AIs, start or set session windows, and change settings. Every change is
// saved as you make it.
public partial class MainWindow : FluentWindow
{
    readonly AppController app;
    ResetView? current;
    bool loading;

    public MainWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        DataContext = app;
        for (int i = 0; i < 7; i++) DayBox.Items.Add(((DayOfWeek)((i + 1) % 7)).ToString());  // Monday first
        TimeBox.ValueChanged += Time_Changed;
        SessionTimeBox.Value = DateTime.Now.AddHours(5).TimeOfDay;
        BankedDate.SelectedDate = DateTime.Today.AddDays(7);
        BankedDate.DisplayDateStart = DateTime.Today;
        DataPath.Text = Store.Dir;
        VersionText.Text = "Cooldown " + Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) + "  ·  MIT license";
        // updateAccents: false, so Windows' accent color doesn't replace Cooldown's mint.
        Loaded += (_, _) => SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: false);
        app.Resets.CollectionChanged += (_, _) => { if (current != null && !app.Resets.Contains(current)) Select(app.Resets.FirstOrDefault()); };

        if (app.Resets.Count > 0) Select(app.Resets[0]);
        else ShowPanel(EmptyPanel);
        RestoreBounds_();
        Loaded += (_, _) => NameLibraryParts();
        StateChanged += (_, _) => NameLibraryParts();  // Maximize becomes Restore
    }

    // Some buttons inside WPF-UI's controls have no name for screen readers (the title bar's) or are
    // named after their icon ("CaretUp24"). Their template parts have predictable names, so name them.
    void NameLibraryParts()
    {
        foreach (DependencyObject part in Descendants(this))
        {
            // Scrollbar arrows and tracks are named after their icons, or not at all.
            if (part is System.Windows.Controls.Primitives.RepeatButton scroll)
            {
                string? what = scroll.Command == System.Windows.Controls.Primitives.ScrollBar.LineUpCommand ? "Scroll up"
                    : scroll.Command == System.Windows.Controls.Primitives.ScrollBar.LineDownCommand ? "Scroll down"
                    : scroll.Command == System.Windows.Controls.Primitives.ScrollBar.PageUpCommand ? "Page up"
                    : scroll.Command == System.Windows.Controls.Primitives.ScrollBar.PageDownCommand ? "Page down" : null;
                if (what != null) { System.Windows.Automation.AutomationProperties.SetName(scroll, what); continue; }
            }
            if (part is not FrameworkElement el || el.Name.Length == 0) continue;
            string owner = el.TemplatedParent is FrameworkElement t ? System.Windows.Automation.AutomationProperties.GetName(t) : "";
            string? name = el.Name switch
            {
                "PART_MinimizeButton" => "Minimize",
                "PART_MaximizeButton" => WindowState == WindowState.Maximized ? "Restore" : "Maximize",
                "PART_CloseButton" => "Close",
                "PART_InlineIncrementButton" or "PART_CompactIncrementButton" => "Increase " + owner,
                "PART_InlineDecrementButton" or "PART_CompactDecrementButton" => "Decrease " + owner,
                "PART_ClearButton" => "Clear " + owner,
                _ => null,
            };
            if (name != null) System.Windows.Automation.AutomationProperties.SetName(el, name.Trim());
        }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject d in Descendants(child)) yield return d;
        }
    }

    // ---- size and position ----

    // Reopens where it was last closed, unless that spot is now off-screen (a monitor unplugged).
    void RestoreBounds_()
    {
        if (app.Settings.WindowBounds is not { } b) return;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(b.X + 40, b.Y, Math.Max(1, b.Width - 80), 32);  // enough of it to drag back
        if (!screen.IntersectsWith(titleBar)) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = b.X;
        Top = b.Y;
        Width = Math.Max(MinWidth, b.Width);
        Height = Math.Max(MinHeight, b.Height);
        if (app.Settings.WindowMaximized) Loaded += (_, _) => WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        Rect b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!b.IsEmpty) app.SaveWindow(b, WindowState == WindowState.Maximized);
    }

    // ---- navigation ----

    public void Select(ResetView? view)
    {
        if (view == null) { current = null; List.SelectedItem = null; ShowPanel(EmptyPanel); return; }
        List.SelectedItem = view;  // runs List_SelectionChanged
    }

    public void ShowSettings()
    {
        List.SelectedItem = null;
        current = null;
        loading = true;
        ModeTaskbar.IsChecked = app.Settings.Mode == DisplayMode.Taskbar;
        ModeIsland.IsChecked = app.Settings.Mode == DisplayMode.Island;
        ModeTray.IsChecked = app.Settings.Mode == DisplayMode.Tray;
        AmPmSwitch.IsChecked = app.Settings.Use12h;
        NotifySwitch.IsChecked = app.Settings.Notify;
        HoverSwitch.IsChecked = app.Settings.HoverOpen;
        SelectByTag(RemindWeekBox, app.Settings.RemindWeekMinutes);
        SelectByTag(RemindSessionBox, app.Settings.RemindSessionMinutes);
        SelectByTag(RemindBankedBox, app.Settings.RemindBankedMinutes);
        RemindersPanel.IsEnabled = app.Settings.Notify;
        StartupSwitch.IsChecked = Startup.Enabled;
        loading = false;
        ShowPanel(SettingsPanel);
    }

    public void ShowWelcome() => WelcomeBar.IsOpen = true;

    void ShowPanel(FrameworkElement panel)
    {
        foreach (FrameworkElement p in new FrameworkElement[] { EmptyPanel, ResetPanel, SettingsPanel })
            p.Visibility = p == panel ? Visibility.Visible : Visibility.Collapsed;
        SettingsButton.Appearance = panel == SettingsPanel ? ControlAppearance.Secondary : ControlAppearance.Transparent;
    }

    void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItem is not ResetView view)
        {
            if (current != null && SettingsPanel.Visibility != Visibility.Visible) ShowPanel(app.Resets.Count == 0 ? EmptyPanel : ResetPanel);
            return;
        }
        current = view;
        ResetPanel.DataContext = view;
        loading = true;
        NameBox.Text = view.Model.Name;
        NameError.Visibility = Visibility.Collapsed;
        DayBox.SelectedIndex = ((int)view.Model.Day + 6) % 7;
        TimeBox.ApplyClockFormat();
        TimeBox.Value = view.Model.Time;
        SessionSwitch.IsChecked = view.Model.SessionHours > 0;
        HoursBox.Value = view.Model.SessionHours > 0 ? view.Model.SessionHours : 5;
        HoursBox.IsEnabled = view.Model.SessionHours > 0;
        SessionTimeBox.ApplyClockFormat();
        SessionTimeBox.Value = (view.Model.SessionEnd ?? DateTime.Now.AddHours(Math.Max(1, view.Model.SessionHours))).TimeOfDay;
        BankedTime.ApplyClockFormat();
        BankedTime.Value = view.Model.Time;
        BankedError.Visibility = Visibility.Collapsed;
        BankedList.Items.Refresh();  // their times, after a change between 12- and 24-hour time
        loading = false;
        ShowPanel(ResetPanel);
    }

    void Settings_Click(object sender, RoutedEventArgs e) => ShowSettings();

    // ---- editing an AI ----

    void Add_Click(object sender, RoutedEventArgs e)
    {
        ResetView view = app.Add();
        Select(view);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        ResetView view = current;
        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Remove " + view.Name + "?",
            Content = "Its countdown, any running session and its banked resets will be removed.",
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        int index = app.Resets.IndexOf(view);
        app.Remove(view);
        Select(app.Resets.Count == 0 ? null : app.Resets[Math.Min(index, app.Resets.Count - 1)]);
    }

    // Saved as you type, unless the name is empty or already used: sessions are saved by name.
    void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (loading || current == null) return;
        string name = NameBox.Text.Trim();
        string? problem = name.Length == 0 ? "Give it a name."
            : app.NameTaken(name, current) ? "You already have an AI called " + name + "." : null;
        NameError.Text = problem ?? "";
        NameError.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        if (problem != null) return;
        current.Model.Name = Store.CleanName(name);  // as it's saved, so the list and the file agree
        app.Save();
    }

    void Day_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || current == null || DayBox.SelectedIndex < 0) return;
        current.Model.Day = (DayOfWeek)((DayBox.SelectedIndex + 1) % 7);
        app.ScheduleChanged(current);
    }

    void Time_Changed(TimeSpan t)
    {
        if (loading || current == null) return;
        current.Model.Time = t;
        app.ScheduleChanged(current);
    }

    void Session_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading || current == null) return;
        bool on = SessionSwitch.IsChecked == true;
        HoursBox.IsEnabled = on;
        current.Model.SessionHours = on ? (int)(HoursBox.Value ?? 5) : 0;
        if (!on) current.Model.SessionEnd = null;
        app.Save();
    }

    void Hours_Changed(object sender, RoutedEventArgs e)
    {
        if (loading || current == null || SessionSwitch.IsChecked != true || HoursBox.Value is not double h) return;
        current.Model.SessionHours = (int)Math.Clamp(h, 1, 24);
        app.Save();
    }

    void SetSession_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        DateTime now = DateTime.Now, end = now.Date.Add(SessionTimeBox.Value);
        app.SetSessionEnd(current, end <= now ? end.AddDays(1) : end);
    }

    void EndSession_Click(object sender, RoutedEventArgs e)
    {
        if (current != null) app.SetSessionEnd(current, null);
    }

    // ---- banked resets ----

    void AddBanked_Click(object sender, RoutedEventArgs e)
    {
        if (current == null) return;
        DateTime? expires = BankedDate.SelectedDate?.Date.Add(BankedTime.Value);
        string? problem = expires == null ? "Pick the date it expires."
            : expires <= DateTime.Now ? "That time has already passed." : null;
        BankedError.Text = problem ?? "";
        BankedError.Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
        if (problem == null) app.AddBanked(current, expires!.Value);
    }

    void RemoveBanked_Click(object sender, RoutedEventArgs e)
    {
        if (current != null && sender is FrameworkElement { DataContext: Banked banked }) app.RemoveBanked(current, banked);
    }

    // ---- settings ----

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        app.SetMode(sender == ModeIsland ? DisplayMode.Island : sender == ModeTray ? DisplayMode.Tray : DisplayMode.Taskbar);
    }

    void AmPm_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        app.SetUse12h(AmPmSwitch.IsChecked == true);
        TimeBox.ApplyClockFormat();
        SessionTimeBox.ApplyClockFormat();
        BankedTime.ApplyClockFormat();
    }

    void Hover_Toggled(object sender, RoutedEventArgs e)
    {
        if (!loading) app.SetHoverOpen(HoverSwitch.IsChecked == true);
    }

    void Notify_Toggled(object sender, RoutedEventArgs e)
    {
        RemindersPanel.IsEnabled = NotifySwitch.IsChecked == true;  // reminders are notifications too
        if (!loading) app.SetNotify(NotifySwitch.IsChecked == true);
    }

    void TestNotify_Click(object sender, RoutedEventArgs e) => app.SendTestNotification();

    void Remind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (loading || RemindWeekBox.SelectedItem == null || RemindSessionBox.SelectedItem == null || RemindBankedBox.SelectedItem == null) return;
        app.SetReminders(TagOf(RemindWeekBox), TagOf(RemindSessionBox), TagOf(RemindBankedBox));
    }

    static int TagOf(ComboBox box) => box.SelectedItem is ComboBoxItem { Tag: string t } ? int.Parse(t) : 0;

    // Selects the choice for a saved value, or Off if the value isn't one of the choices.
    static void SelectByTag(ComboBox box, int minutes)
    {
        box.SelectedIndex = 0;
        foreach (ComboBoxItem item in box.Items)
            if (item.Tag is string t && int.Parse(t) == minutes) box.SelectedItem = item;
    }

    void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (loading) return;
        try { Startup.Enabled = StartupSwitch.IsChecked == true; }
        catch (Exception) { loading = true; StartupSwitch.IsChecked = Startup.Enabled; loading = false; }
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Store.Dir + "\"") { UseShellExecute = true });
}
