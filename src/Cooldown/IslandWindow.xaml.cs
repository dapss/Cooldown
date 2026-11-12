using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Cooldown.Core;
using Microsoft.Win32;

namespace Cooldown;

// The island: a small countdown that springs open into a card with every reset. It either sits in
// the taskbar's empty space, or floats anywhere as a black pill.
//
// So it never blocks what's under it:
// - Floating, it fades almost away and lets clicks through when the mouse moves onto it. Resting the
//   mouse on it fades it back in and opens the card; clicking through cancels that.
// - In the taskbar it covers nothing, so it simply opens on hover or click, and it hides with an
//   auto-hiding taskbar.
// - It hides itself while a full-screen app (a game, a video, a presentation) is in front.
//
// Layout is in WPF units (DIPs) inside the window; the window itself is placed in screen pixels.
public partial class IslandWindow : Window
{
    const double Margin_ = 18, PillH = 30, CardW = 320;
    const double OpenAfterMs = 650, GrabAfterMs = 300, DockedOpenAfterMs = 300, CloseAfterMs = 300, GhostOpacity = 0.12;

    static readonly Color Fill = Color.FromRgb(14, 14, 16);
    static readonly Brush Ink = Frozen(Color.FromRgb(242, 242, 247)), Muted = Frozen(Color.FromRgb(160, 160, 168));
    static readonly Brush Green = Frozen(Color.FromRgb(120, 200, 150)), Orange = Frozen(Color.FromRgb(255, 159, 10));
    static readonly Brush Track = Frozen(Color.FromRgb(46, 46, 52));

    readonly AppController app;
    readonly IntPtr hwnd;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly DispatcherTimer watch = new() { Interval = TimeSpan.FromMilliseconds(150) };
    readonly Native.WinEventProc foregroundHook;
    double scale = 1;

    // Placement
    bool docked, growDown = true;
    Point anchor;               // floating: the pill's top-center, in screen pixels
    int dockFromRight = -1;     // docked: the pill's center, in pixels from the taskbar's right end
    Native.RECT taskbar;
    bool taskbarHidden, lightTaskbar;
    double winW, winH, pillW = 120, cardH = 200, pillCX, pillY, cardY;  // DIPs, window coordinates

    // The window keeps some empty, click-through room past the card's far edge, so the open card can
    // grow a little (a session's editor opening, as it animates) without the window moving or
    // resizing, which shows as a jump. Placing it again is for bigger changes, or while it's closed.
    double placedH = 200;  // the card height the window is sized and placed for, headroom included
    const double Headroom = 90;
    Rect shape;

    // Open / close animation: 0 = collapsed, 1 = open
    double value, from, target, animStart, animLength;
    bool animating, opening;

    // Getting out of the way
    bool onPill, clickedThrough, fullScreen, clickThrough, hoverPill, noHoverOpen;
    Native.POINT restAt;
    double restSince, offCardSince = -1, lastTopmost, watchCloselyUntil;
    double ghost, ghostTarget;  // 0 = solid, 1 = faded away

    // Dragging along the taskbar
    bool dockDragging, mouseDown;
    double grabDX, downX;

    // Dragging the floating island by its open card
    bool floatPressed, floatDragging, grabbedCard;
    bool pillGrab, justDropped;  // the pill has turned solid under a resting mouse, so it can be grabbed
    Native.POINT pressAt, grabOffset;  // screen pixels; the offset is from the window's top-left

    public IslandWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        CardList.ItemsSource = app.Resets;
        hwnd = new WindowInteropHelper(this).EnsureHandle();        // No Alt+Tab entry, and never takes focus from the app you're using.
        int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        // Mid-drag the drag itself places the window; placing it here would snap it back.
        DpiChanged += (_, e) => { scale = e.NewDpi.DpiScaleX; if (!floatDragging) Place(); Layout(); };

        watch.Tick += (_, _) => Watch();
        IsVisibleChanged += (_, _) => { if (IsVisible) watch.Start(); else watch.Stop(); };
        // Clicking the taskbar, or switching apps, raises the taskbar over every other always-on-top
        // window, often a moment after the switch itself. So step back in front at once, then keep a
        // close watch for a second to catch the taskbar rising again. Not for our own windows, such as
        // the menu, which must stay in front of the island.
        foregroundHook = (_, _, fg, _, _, _, _) =>
        {
            Native.GetWindowThreadProcessId(fg, out int pid);
            if (!docked || !IsVisible || pid == Environment.ProcessId) return;
            KeepOnTop();
            watchCloselyUntil = Ms + 1000;
            watch.Interval = TimeSpan.FromMilliseconds(30);
        };
        Native.SetWinEventHook(3, 3, IntPtr.Zero, foregroundHook, 0, 0, 0);  // EVENT_SYSTEM_FOREGROUND
        app.Resets.CollectionChanged += (_, _) => { if (IsVisible) Remeasure(); };
        // The card changes height on its own, such as when a session's editor opens: fit it at once.
        CardPanel.SizeChanged += (_, e) =>
        {
            if (IsVisible && e.HeightChanged && Math.Ceiling(e.NewSize.Height) != cardH && !floatDragging) Remeasure();
        };
    }

    double Ms => clock.Elapsed.TotalMilliseconds;
    static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // ---- showing ----

    public void ShowFloating(Point? saved)
    {
        docked = false;
        anchor = saved is { } p && OnScreen(p) ? p : DefaultAnchor();
        Open();
    }

    // fromRight < 0 picks the empty space just left of the clock. A taskbar on the left or right of
    // the screen has no room, so it floats instead.
    public void ShowDocked(int fromRight)
    {
        dockFromRight = fromRight;
        docked = RefreshTaskbar() && taskbar.Right - taskbar.Left > taskbar.Bottom - taskbar.Top;
        if (!docked) anchor = DefaultAnchor();
        lightTaskbar = LightTaskbar();
        Open();
    }

    void Open()
    {
        animating = false;
        CompositionTarget.Rendering -= OnFrame;
        value = target = 0;
        onPill = hoverPill = dockDragging = pillGrab = justDropped = false;
        ghost = ghostTarget = 0;
        Show();
        Remeasure();
        Apply();
        if (docked) KeepOnTop();
    }

    Point DefaultAnchor()
    {
        var wa = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        return new Point(wa.Left + wa.Width / 2, wa.Top + 10 * scale);
    }

    static bool OnScreen(Point p) =>
        System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.Contains((int)p.X, (int)p.Y));

    void KeepOnTop()
    {
        lastTopmost = Ms;
        Native.SetWindowPos(hwnd, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);  // HWND_TOPMOST; no size, move or activate
    }

    // Whether the taskbar is in front of the island, covering it. Only the few windows above the
    // island are looked at.
    bool TaskbarInFront()
    {
        IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
        for (IntPtr h = Native.GetWindow(hwnd, 3); h != IntPtr.Zero; h = Native.GetWindow(h, 3))  // GW_HWNDPREV
            if (h == tb) return true;
        return false;
    }

    // Reads where the taskbar is and whether it has slid away (auto-hide).
    bool RefreshTaskbar()
    {
        IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero || !Native.GetWindowRect(tb, out Native.RECT r)) return false;
        var screen = System.Windows.Forms.Screen.FromHandle(tb).Bounds;
        int seenW = Math.Min(r.Right, screen.Right) - Math.Max(r.Left, screen.Left);
        int seenH = Math.Min(r.Bottom, screen.Bottom) - Math.Max(r.Top, screen.Top);
        taskbarHidden = Math.Min(seenW, seenH) < Math.Min(r.Right - r.Left, r.Bottom - r.Top) / 2;
        bool moved = r.Left != taskbar.Left || r.Top != taskbar.Top || r.Right != taskbar.Right || r.Bottom != taskbar.Bottom;
        taskbar = r;
        if (moved && docked && IsVisible) { Place(); Layout(); }
        return true;
    }

    static bool LightTaskbar()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) is int v && v == 1;
        }
        catch (Exception) { return false; }
    }

    // ---- measuring and placing ----

    // For when the content changes size, such as a session starting or an AI added.
    void Remeasure()
    {
        UpdatePill();
        CardEmpty.Visibility = app.Resets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardList.Visibility = app.Resets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        CardPanel.Measure(new Size(CardW, double.PositiveInfinity));
        cardH = Math.Ceiling(CardPanel.DesiredSize.Height);
        bool shut = value == 0 && !animating;
        if (shut || cardH > placedH || cardH < placedH - Headroom * 2) Place();
        Layout();
    }

    void Place()
    {
        winW = CardW + Margin_ * 2;
        placedH = cardH + Headroom;
        if (docked) { PlaceDocked(); return; }
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)anchor.X, (int)anchor.Y)).WorkingArea;
        if (!floatDragging) growDown = anchor.Y < wa.Top + wa.Height / 2;  // mid-drag, the drag decides
        winH = placedH + Margin_ * 2;
        pillCX = winW / 2;
        pillY = growDown ? Margin_ : winH - Margin_ - PillH;
        cardY = growDown ? Margin_ : winH - Margin_ - placedH;
        double left = anchor.X / scale - winW / 2;
        double top = growDown ? anchor.Y / scale - Margin_ : anchor.Y / scale + PillH + Margin_ - winH;
        SetBounds(left, top);
    }

    // The pill is centered in the taskbar's height; the card opens just above it (or below a top
    // taskbar), kept on screen even when the pill is near the end of the taskbar.
    void PlaceDocked()
    {
        double tl = taskbar.Left / scale, tt = taskbar.Top / scale, tr = taskbar.Right / scale, tbm = taskbar.Bottom / scale;
        var sb = System.Windows.Forms.Screen.FromHandle(Native.FindWindow("Shell_TrayWnd", null)).Bounds;
        double sl = sb.Left / scale, sr = sb.Right / scale, st = sb.Top / scale, sh = sb.Height / scale;
        if (dockFromRight < 0) dockFromRight = DefaultFromRight();
        double half = pillW / 2 + 6;
        double cx = Math.Clamp(tr - dockFromRight / scale, tl + half, tr - half);
        bool bottom = tt > st + sh / 2;
        growDown = !bottom;
        double pillTop = tt + (tbm - tt - PillH) / 2;
        double cardTop = bottom ? tt - 12 - placedH : tbm + 12;
        double winTop = (bottom ? cardTop : pillTop) - Margin_;
        winH = (bottom ? pillTop + PillH : cardTop + placedH) + Margin_ - winTop;
        double winLeft = Math.Clamp(cx - winW / 2, sl - Margin_ + 12, sr + Margin_ - 12 - winW);
        pillCX = cx - winLeft;
        pillY = pillTop - winTop;
        cardY = cardTop - winTop;
        SetBounds(winLeft, winTop);
    }

    int DefaultFromRight()
    {
        IntPtr tray = Native.FindWindowEx(Native.FindWindow("Shell_TrayWnd", null), IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = tray != IntPtr.Zero && Native.GetWindowRect(tray, out Native.RECT r) ? r.Left : taskbar.Right - (int)(300 * scale);
        return taskbar.Right - trayLeft + (int)((16 + pillW / 2) * scale);
    }

    void SetBounds(double left, double top)
    {
        Left = Math.Round(left);
        Top = Math.Round(top);
        Width = winW;
        Height = winH;
        Root.Width = winW;
        Root.Height = winH;
    }

    // Next to the pill: at the top of the room the window keeps for it when opening downward, at the
    // bottom when opening upward. Its edge by the pill stays put as its height changes.
    Rect CardRect() => new(winW / 2 - CardW / 2, growDown ? cardY : cardY + placedH - cardH, CardW, cardH);
    Rect PillRect() => new(pillCX - pillW / 2, pillY, pillW, PillH);

    // ---- the pill ----

    // What the collapsed pill shows: the soonest running session, or else the soonest weekly reset.
    void UpdatePill()
    {
        DateTime now = DateTime.Now;
        ResetView? best = app.Resets.Where(v => v.Model.SessionEnd.HasValue).MinBy(v => v.Model.SessionEnd!.Value);
        bool session = best != null;
        best ??= app.Resets.MinBy(v => v.Model.Next(now));

        bool light = docked && lightTaskbar;
        PillName.Foreground = light ? Frozen(Color.FromRgb(80, 80, 88)) : Muted;
        PillRing.Track = light ? Frozen(Color.FromRgb(205, 205, 210)) : Track;
        if (best == null)
        {
            PillName.Text = "No AIs yet";
            PillCount.Text = "";
            PillRing.Fraction = 0;
        }
        else
        {
            Reset r = best.Model;
            TimeSpan left = session ? r.SessionEnd!.Value - now : r.Next(now) - now;
            PillName.Text = r.Name;
            PillCount.Text = Format.Compact(left);
            PillRing.Fraction = session ? 1 - left.TotalHours / r.SessionHours : 1 - left.TotalDays / 7;
            Brush count = session ? Orange : left.TotalHours < 24 ? Green : Ink;
            if (light) count = session ? Frozen(Color.FromRgb(190, 100, 0)) : left.TotalHours < 24 ? Frozen(Color.FromRgb(25, 125, 65)) : Frozen(Color.FromRgb(24, 24, 28));
            PillCount.Foreground = count;
            PillRing.Fill = count == Ink ? Green : count;
        }
        PillCount.Visibility = PillCount.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        System.Windows.Automation.AutomationProperties.SetName(PillPanel,
            PillCount.Text.Length == 0 ? PillName.Text : PillName.Text + ", " + PillCount.Text + " left");
        PillPanel.Measure(new Size(double.PositiveInfinity, PillH));
        pillW = Math.Ceiling(PillPanel.DesiredSize.Width);
    }

    public void UpdateTimes()
    {
        if (!IsVisible) return;
        CheckFullScreen();
        if (docked)
        {
            bool light = LightTaskbar();
            if (light != lightTaskbar) lightTaskbar = light;
        }
        if (animating || Gone) return;
        double oldPill = pillW;
        UpdatePill();
        CardPanel.Measure(new Size(CardW, double.PositiveInfinity));
        if (Math.Ceiling(CardPanel.DesiredSize.Height) != cardH || pillW != oldPill) Remeasure();  // a session started or ended
        else Layout();
    }

    // ---- drawing ----

    static double Lerp(double a, double b, double t) => a + (b - a) * t;
    static double Clamp01(double v) => Math.Clamp(v, 0, 1);

    static Color LerpColor(Color a, Color b, double t)
    {
        t = Clamp01(t);
        return Color.FromArgb((byte)Lerp(a.A, b.A, t), (byte)Lerp(a.R, b.R, t), (byte)Lerp(a.G, b.G, t), (byte)Lerp(a.B, b.B, t));
    }

    void Layout()
    {
        double v = value;
        Rect a = PillRect(), b = CardRect();
        shape = new Rect(Math.Round(Lerp(a.X, b.X, v)), Math.Round(Lerp(a.Y, b.Y, v)),
            Math.Max(1, Math.Round(Lerp(a.Width, b.Width, v))), Math.Max(1, Math.Round(Lerp(a.Height, b.Height, v))));
        double radius = Math.Min(Lerp(docked ? 6 : PillH / 2, 20, Clamp01(v)), shape.Height / 2);

        // In the taskbar the collapsed pill has no background of its own, only a hover highlight like a
        // taskbar button's. (Alpha 1, not 0, so it still catches the mouse.)
        double solid = docked ? Clamp01(v * 2) : 1;
        Color pillBack = !hoverPill ? Color.FromArgb(1, 0, 0, 0) : lightTaskbar ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(26, 255, 255, 255);
        Shape.Background = new SolidColorBrush(docked ? LerpColor(pillBack, Fill, solid) : Fill);
        Shadow.Opacity = solid;
        Outline.Opacity = solid;

        foreach (var border in new[] { Shadow, Shape, Outline })
        {
            System.Windows.Controls.Canvas.SetLeft(border, shape.X);
            System.Windows.Controls.Canvas.SetTop(border, shape.Y);
            border.Width = shape.Width;
            border.Height = shape.Height;
            border.CornerRadius = new CornerRadius(radius);
        }
        Shape.Clip = new RectangleGeometry(new Rect(0, 0, shape.Width, shape.Height), radius, radius);

        double pillAlpha = Clamp01(1 - v * 3), cardAlpha = Clamp01((v - 0.3) / 0.6);
        PillPanel.Opacity = pillAlpha;
        System.Windows.Controls.Canvas.SetLeft(PillPanel, Math.Round((shape.Width - pillW) / 2));
        System.Windows.Controls.Canvas.SetTop(PillPanel, growDown ? 0 : shape.Height - PillH);
        CardPanel.Opacity = cardAlpha;
        CardPanel.Visibility = cardAlpha > 0 ? Visibility.Visible : Visibility.Hidden;
        CardPanel.IsHitTestVisible = !animating && value >= 1;
        double slide = Math.Round((1 - cardAlpha) * 8 * (growDown ? -1 : 1));
        System.Windows.Controls.Canvas.SetLeft(CardPanel, Math.Round((shape.Width - CardW) / 2));
        System.Windows.Controls.Canvas.SetTop(CardPanel, (growDown ? 0 : shape.Height - cardH) + slide);
    }

    // ---- open / close animation ----

    void Animate(bool open)
    {
        double to = open ? 1 : 0;
        if (to == target) return;
        if (!open) foreach (ResetView v in app.Resets) v.CancelEdit();  // closing the card drops an unsaved change
        if (open) { UpdatePill(); Remeasure(); }
        from = value;
        target = to;
        opening = open;
        if (!Motion)
        {
            // Animation effects are off in Windows: switch straight to the new state.
            value = target;
            Layout();
            return;
        }
        animStart = Ms;
        animLength = open ? 420 : 260;
        if (!animating) { animating = true; CompositionTarget.Rendering += OnFrame; }
    }

    // Windows' "Animation effects" setting (Settings > Accessibility > Visual effects).
    static bool Motion => SystemParameters.ClientAreaAnimation;

    void OnFrame(object? sender, EventArgs e)
    {
        double p = Math.Min(1, (Ms - animStart) / animLength);
        double eased = opening ? EaseOutBack(p) : 1 - Math.Pow(1 - p, 3);
        value = from + (target - from) * eased;
        if (p >= 1)
        {
            value = target;
            animating = false;
            CompositionTarget.Rendering -= OnFrame;
        }
        Layout();
    }

    // Overshoots by about 4% before settling: the "spring" in the opening animation.
    static double EaseOutBack(double p)
    {
        const double c1 = 1.05, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(p - 1, 3) + c1 * Math.Pow(p - 1, 2);
    }

    // ---- getting out of the way ----

    // Hidden: behind a full-screen app, or in an auto-hidden taskbar that has slid away.
    bool Gone => fullScreen || (docked && taskbarHidden && target == 0 && !animating);

    void CheckFullScreen()
    {
        bool now = Native.FullScreenAppInFront;
        if (now == fullScreen) return;
        fullScreen = now;
        if (fullScreen)
        {
            animating = false;
            CompositionTarget.Rendering -= OnFrame;
            value = target = 0;
            onPill = pillGrab = justDropped = false;
            ghost = ghostTarget = 0;
            Layout();
        }
        Apply();
    }

    Rect ToScreen(Rect r)
    {
        Native.GetWindowRect(hwnd, out Native.RECT w);
        return new Rect(w.Left + r.X * scale, w.Top + r.Y * scale, r.Width * scale, r.Height * scale);
    }

    // Runs a few times a second; quicker while the mouse is on the island or it's fading.
    void Watch()
    {
        if (fullScreen) { Pace(150); return; }
        Native.GetCursorPos(out Native.POINT c);
        var cursor = new Point(c.X, c.Y);
        double now = Ms;

        if (docked)
        {
            bool wasHidden = taskbarHidden;
            RefreshTaskbar();
            if (taskbarHidden != wasHidden) Apply();
            if (Gone) { Pace(150); return; }
            if (!app.MenuOpen && (now - lastTopmost > 500 || TaskbarInFront())) KeepOnTop();
        }

        if (docked && target == 0 && !animating)
        {
            // Nothing is under it here, so no fading. It opens on click, and on hover too if that's on in
            // Settings: off by default, since the mouse often passes over it on the way to the tray.
            bool inside = ToScreen(PillRect()).Contains(cursor);
            if (inside != hoverPill) { hoverPill = inside; Layout(); }
            if (inside && !onPill) { onPill = true; restAt = c; restSince = now; }
            else if (!inside) onPill = noHoverOpen = false;
            if (onPill && app.Settings.HoverOpen && !dockDragging && !mouseDown && !noHoverOpen && !app.MenuOpen)
            {
                if (Math.Abs(c.X - restAt.X) > 4 * scale || Math.Abs(c.Y - restAt.Y) > 4 * scale) { restAt = c; restSince = now; }
                if (now - restSince >= DockedOpenAfterMs) { onPill = hoverPill = false; offCardSince = -1; Animate(true); return; }
            }
        }
        else if (target == 0 && !animating)
        {
            Rect pill = ToScreen(PillRect());
            pill.Inflate(3 * scale, 3 * scale);
            if (floatPressed || pill.Contains(cursor))
            {
                if (!onPill) { onPill = true; clickedThrough = false; restAt = c; restSince = now; }
                if (Native.MouseButtonDown && !pillGrab) clickedThrough = true;
                if (Math.Abs(c.X - restAt.X) > 4 * scale || Math.Abs(c.Y - restAt.Y) > 4 * scale) { restAt = c; restSince = now; }
                double rest = now - restSince;
                if (floatPressed || justDropped) ghostTarget = 0;  // being moved, or just put down
                else if (clickedThrough) ghostTarget = 1;
                else if (rest >= OpenAfterMs)
                {
                    onPill = pillGrab = false;
                    ghost = ghostTarget = 0;
                    offCardSince = -1;
                    Apply();
                    Animate(true);
                    return;
                }
                else
                {
                    // Fades away at once, then back in while the mouse rests: the cue that it's about to
                    // open. Partway there it turns solid and can be grabbed, and stays so while on it.
                    if (rest >= GrabAfterMs) pillGrab = true;
                    ghostTarget = pillGrab ? 0 : rest < 180 ? 1 : 1 - (rest - 180) / (OpenAfterMs - 180);
                }
            }
            else
            {
                onPill = pillGrab = justDropped = false;
                ghostTarget = 0;
            }
        }
        else if (target == 1)
        {
            // The card, the pill, and the gap between them in the taskbar.
            Rect area = ToScreen(Rect.Union(CardRect(), PillRect()));
            area.Inflate(8 * scale, 8 * scale);
            bool busy = mouseDown || app.MenuOpen || demoHold;
            if (area.Contains(cursor) || busy) offCardSince = -1;
            else if (offCardSince < 0) offCardSince = now;
            else if (now - offCardSince >= CloseAfterMs && !animating)
            {
                offCardSince = -1;
                Animate(false);
            }
        }

        // Ease the fade toward its target.
        ghost = Motion && Math.Abs(ghostTarget - ghost) > 0.01 ? ghost + (ghostTarget - ghost) * 0.4 : ghostTarget;
        Apply();

        Pace(onPill || target == 1 || ghost != ghostTarget || (docked && now < watchCloselyUntil) ? 30 : docked ? 100 : 150);
    }

    void Pace(double ms)
    {
        if (watch.Interval.TotalMilliseconds != ms) watch.Interval = TimeSpan.FromMilliseconds(ms);
    }

    // Applies the fade and click-through for the current state.
    void Apply()
    {
        bool through = Gone || (!pillGrab && (ghost > 0.02 || ghostTarget > 0));
        if (through != clickThrough)
        {
            clickThrough = through;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, through ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT);
        }
        Opacity = Gone ? 0 : 1 - (1 - GhostOpacity) * ghost;
    }

    // ---- mouse (only reaches the open card, or the pill in the taskbar) ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        mouseDown = true;
        Point p = e.GetPosition(this);
        downX = p.X;
        grabDX = p.X - pillCX;
        if (docked && target == 0) CaptureMouse();
    }

    // ---- moving the floating island ----
    //
    // Grab the open card anywhere, rows and buttons included, or the pill once the mouse has rested on
    // it long enough for it to turn solid. A press that doesn't move past a few pixels is an ordinary
    // click (on the pill, it opens the card). The island follows the mouse exactly, stays whole on the
    // screen it's over, and lines up with the screen's center when it comes close. Dragged into the
    // bottom half, the card switches to opening upward, and back again at the top.

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (docked) return;
        if (target == 1 && value >= 0.9) grabbedCard = true;
        else if (target == 0 && !animating && pillGrab) grabbedCard = false;
        else return;
        floatPressed = mouseDown = true;
        Native.GetCursorPos(out pressAt);
        Rect r = ToScreen(grabbedCard ? CardRect() : PillRect());
        grabOffset = new Native.POINT { X = pressAt.X - (int)r.X, Y = pressAt.Y - (int)r.Y };
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!floatPressed) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndFloatDrag(); return; }
        Native.GetCursorPos(out Native.POINT c);
        if (!floatDragging)
        {
            if (Math.Abs(c.X - pressAt.X) < 4 * scale && Math.Abs(c.Y - pressAt.Y) < 4 * scale) return;
            floatDragging = true;
            CaptureMouse();  // takes the press from any button under it, so letting go won't click it
            Mouse.OverrideCursor = Cursors.SizeAll;
        }
        MoveTo(c);
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (!floatPressed) return;
        e.Handled = floatDragging || !grabbedCard;
        EndFloatDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Only the window's own capture: a button losing it to the window as a drag starts bubbles here too.
        if (floatDragging && e.OriginalSource == this) EndFloatDrag();
    }

    void MoveTo(Native.POINT cursor)
    {
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        Rect grabbed = grabbedCard ? CardRect() : PillRect();
        double top = cursor.Y - grabOffset.Y, cx = cursor.X - grabOffset.X + grabbed.Width * scale / 2;

        // Room for the card wherever it ends up, and the grabbed part whole on screen.
        double half = CardW / 2 * scale;
        cx = Math.Clamp(cx, wa.Left + half, Math.Max(wa.Left + half, wa.Right - half));
        top = Math.Clamp(top, wa.Top, Math.Max(wa.Top, wa.Bottom - grabbed.Height * scale));

        double screenCenter = wa.Left + wa.Width / 2.0, middle = wa.Top + wa.Height / 2.0;
        if (Math.Abs(cx - screenCenter) < 16 * scale) cx = screenCenter;

        // The pill sits at the top of the card when it opens downward, at the bottom when it opens
        // upward; the card itself stays under the mouse either way. Near the middle, where both fit
        // the rule, it keeps the side it has, so it doesn't flicker between them.
        double anchorY = top;
        if (grabbedCard)
        {
            double downY = top, upY = top + (cardH - PillH) * scale;
            bool downFits = downY < middle, upFits = upY >= middle;
            if (downFits != upFits) growDown = downFits;
            anchorY = growDown ? downY : upY;
        }
        else growDown = top < middle;

        anchor = new Point(Math.Round(cx), Math.Round(anchorY));
        Place();
        Layout();
    }

    void EndFloatDrag()
    {
        bool moved = floatDragging;
        floatPressed = floatDragging = mouseDown = false;
        Mouse.OverrideCursor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (!moved)
        {
            // A click on the pill opens the card.
            if (!grabbedCard) { onPill = pillGrab = false; ghost = ghostTarget = 0; offCardSince = -1; Apply(); Animate(true); }
            return;
        }
        app.IslandMoved(anchor);
        if (!grabbedCard) justDropped = true;  // stays solid under the mouse, without opening, until it leaves
        Place();
        Layout();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!docked || !mouseDown || target != 0) return;
        Point p = e.GetPosition(this);
        if (!dockDragging && Math.Abs(p.X - downX) < 4) return;
        // Slides along the taskbar only.
        dockDragging = true;
        Native.GetCursorPos(out Native.POINT c);
        dockFromRight = taskbar.Right - (int)(c.X - grabDX * scale);
        Place();
        Layout();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        bool wasDown = mouseDown;
        mouseDown = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (dockDragging)
        {
            dockDragging = false;
            double half = (pillW / 2 + 6) * scale;
            dockFromRight = (int)Math.Clamp(dockFromRight, half, taskbar.Right - taskbar.Left - half);
            app.IslandDocked(dockFromRight);
            return;
        }
        if (wasDown && docked && target == 0) { onPill = false; Animate(true); }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        noHoverOpen = true;  // a right-click means the menu, not the card, until the mouse leaves
        app.ShowMenu();
    }

#if DEBUG
    bool demoHold;  // --demo-editor: the card stays open wherever the mouse is

    public void DemoHoldOpen()
    {
        demoHold = true;
        ghost = ghostTarget = 0;
        Apply();
        Animate(true);
    }
#else
    const bool demoHold = false;
#endif

    // Scrolling on the editor's hour or minute steps it, like its arrows.
    void Hour_Wheel(object sender, MouseWheelEventArgs e) => Wheel(sender, e, TimeSpan.FromHours(1));
    void Minute_Wheel(object sender, MouseWheelEventArgs e) => Wheel(sender, e, TimeSpan.FromMinutes(1));

    static void Wheel(object sender, MouseWheelEventArgs e, TimeSpan step)
    {
        if (sender is FrameworkElement { DataContext: ResetView view } && e.Delta != 0) view.Step(e.Delta > 0 ? step : -step);
        e.Handled = true;
    }

    void Edit_Click(object sender, RoutedEventArgs e) => app.OpenMain();
    void More_Click(object sender, RoutedEventArgs e) => app.ShowMenu();
}
