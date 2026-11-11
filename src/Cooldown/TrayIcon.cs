using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using Cooldown.Core;
using Wpf.Ui.Controls;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Cooldown;

// The tray icon. Left-click opens Cooldown; right-click shows a Fluent-styled menu with every
// countdown, the display mode and Exit.
public sealed class TrayIcon : IDisposable
{
    readonly AppController app;
    readonly Forms.NotifyIcon icon = new();
    Drawing.Icon? image;
    string lastIcon = "";
    readonly Window menuHost;  // an invisible window the menu belongs to, so it closes on clicks elsewhere
    string lastTip = "";

    public TrayIcon(AppController app)
    {
        this.app = app;
        UpdateIcon();
        icon.Text = "Cooldown";
        icon.MouseUp += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) app.OpenMain();
            else if (e.Button == Forms.MouseButtons.Right) ShowMenu();
        };
        icon.BalloonTipClicked += (_, _) => app.OpenMain();
        icon.Visible = true;

        menuHost = new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Width = 0, Height = 0, Left = -32000, Top = -32000, AllowsTransparency = true, Background = null,
        };
        // A tool window, or once the menu has brought it to the front, Alt+Tab lists it as an empty,
        // untitled window.
        IntPtr host = new WindowInteropHelper(menuHost).EnsureHandle();
        Native.SetWindowLong(host, Native.GWL_EXSTYLE, Native.GetWindowLong(host, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
        menuHost.Show();
    }

    public void Update()
    {
        var tip = new StringBuilder();
        foreach (ResetView v in app.Resets)
            tip.Append(v.Name).Append(": ").Append(Format.Short(v.Model.Next(DateTime.Now) - DateTime.Now)).Append('\n');
        string text = app.Resets.Count == 0 ? "Cooldown" : tip.ToString().TrimEnd();
        if (text.Length > 127) text = text[..124] + "...";
        if (text != lastTip) { icon.Text = text; lastTip = text; }
        UpdateIcon();
    }

    // The icon is a cooldown ring for whatever the island shows: the soonest running session (orange),
    // or else the soonest weekly reset (mint). It fills up as the reset gets closer, in 24 steps, so
    // it's redrawn only a few times a day.
    void UpdateIcon()
    {
        DateTime now = DateTime.Now;
        ResetView? session = app.Resets.Where(v => v.Model.SessionEnd.HasValue).MinBy(v => v.Model.SessionEnd!.Value);
        ResetView? week = app.Resets.MinBy(v => v.Model.Next(now));
        double fraction = session != null ? session.SessionProgress : week?.WeekProgress ?? 0;
        int steps = (int)Math.Round(fraction * 24);
        string key = (session != null ? "s" : "w") + steps;
        if (key == lastIcon) return;
        lastIcon = key;
        Drawing.Icon? old = image;
        image = DrawRing(steps / 24.0, session != null ? Drawing.Color.FromArgb(255, 159, 10) : Drawing.Color.FromArgb(120, 200, 150));
        icon.Icon = image;
        if (old != null) { Native.DestroyIcon(old.Handle); old.Dispose(); }
    }

    public void Notify(string title, string text) => icon.ShowBalloonTip(10000, title, text, Forms.ToolTipIcon.Info);

    public bool MenuOpen { get; private set; }

    // Also used by the island's right-click and "more" button.
    public void ShowMenu()
    {
        AppController.ApplySystemTheme();
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, StaysOpen = false };
        menu.Opened += (_, _) => MenuOpen = true;
        menu.Closed += (_, _) => MenuOpen = false;

        foreach (ResetView v in app.Resets)
        {
            var item = new MenuItem { Header = v.Name, Command = v.OpenCommand, FontWeight = FontWeights.SemiBold };
            item.SetBinding(MenuItem.InputGestureTextProperty, new Binding(nameof(ResetView.Summary)) { Source = v });
            menu.Items.Add(item);
            if (v.HasSession && !v.SessionRunning)
                menu.Items.Add(new MenuItem { Header = "Start " + v.SessionLabel, Command = v.StartCommand, Padding = new Thickness(28, 0, 0, 0) });
        }
        if (app.Resets.Count == 0) menu.Items.Add(Item("Add an AI...", SymbolRegular.Add24, () => app.OpenMain()));
        if (app.LoadErrors.Count > 0)
            menu.Items.Add(Item($"{app.LoadErrors.Count} line(s) in resets.txt couldn't be read", SymbolRegular.Warning24, () => app.OpenMain()));
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Open Cooldown", SymbolRegular.Open24, () => app.OpenMain()));
        var show = new MenuItem { Header = "Show countdown", Icon = new SymbolIcon { Symbol = SymbolRegular.Eye24 } };
        show.Items.Add(ModeItem(app, "In the taskbar", DisplayMode.Taskbar));
        show.Items.Add(ModeItem(app, "Floating island", DisplayMode.Island));
        show.Items.Add(ModeItem(app, "Tray icon only", DisplayMode.Tray));
        menu.Items.Add(show);
        menu.Items.Add(Item("Settings", SymbolRegular.Settings24, () => app.OpenMain(settings: true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", SymbolRegular.Power24, app.Exit));

        // A menu only notices clicks elsewhere while its app is in front, so bring the host forward.
        Native.SetForegroundWindow(new WindowInteropHelper(menuHost).Handle);
        menu.IsOpen = true;
    }

    static MenuItem Item(string text, SymbolRegular symbol, Action act) =>
        new() { Header = text, Icon = new SymbolIcon { Symbol = symbol }, Command = new RelayCommand(act) };

    static MenuItem ModeItem(AppController app, string text, DisplayMode mode) =>
        new() { Header = text, IsCheckable = true, IsChecked = app.Settings.Mode == mode, Command = new RelayCommand(() => app.SetMode(mode)) };

    // A thick ring: a faint track, and the elapsed part of the wait in color, sweeping clockwise from
    // the top like a game ability's cooldown. Drawn at the tray's own icon size so it stays crisp.
    static Drawing.Icon DrawRing(double fraction, Drawing.Color color)
    {
        int size = Forms.SystemInformation.SmallIconSize.Width;
        using var bmp = new Drawing.Bitmap(size, size);
        using var g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float stroke = Math.Max(2.5f, size / 5.5f), inset = stroke / 2 + 0.5f;
        var box = new Drawing.RectangleF(inset, inset, size - 2 * inset, size - 2 * inset);
        using (var track = new Drawing.Pen(Drawing.Color.FromArgb(110, 140, 140, 150), stroke))
            g.DrawEllipse(track, box);
        if (fraction > 0)
            using (var arc = new Drawing.Pen(color, stroke) { StartCap = Drawing.Drawing2D.LineCap.Round, EndCap = Drawing.Drawing2D.LineCap.Round })
                g.DrawArc(arc, box, -90, (float)(360 * Math.Min(fraction, 0.999)));
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        icon.Visible = false;
        if (image != null) Native.DestroyIcon(image.Handle);
        icon.Dispose();
        menuHost.Close();
    }
}
