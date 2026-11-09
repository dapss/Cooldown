// The island: a small countdown that springs open into a card with every reset. It either floats
// (a black pill that gets out of the way when the mouse is over it) or sits in the taskbar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Drawn with per-pixel alpha (UpdateLayeredWindow), so the edges are smooth and clicks pass through
// the transparent margin around it.
//
// So it never blocks what's under it:
// - When the mouse moves onto the collapsed pill, it fades almost away and lets clicks through.
// - Resting the mouse on it fades it back in and opens the card. Clicking through cancels that.
// - It hides itself while a full-screen app (a game, a video, a presentation) is in front.
//
// In the taskbar it sits in the taskbar's empty space, so it covers nothing, and opens on hover or
// click into a card above the taskbar, like a Windows flyout. Drag it to slide it along the taskbar.
//
// It redraws only while animating, once a second while open, and when the collapsed pill's text
// changes. The mouse is checked a few times a second, and quicker only while it's on the island.
class Island : Form
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int CX, CY; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, int flags);
    delegate void WinEventProc(IntPtr hook, int ev, IntPtr hwnd, int obj, int child, int thread, int time);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(int min, int max, IntPtr module, WinEventProc proc, int pid, int tid, int flags);

    const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20;

    static readonly Color Fill = Color.FromArgb(255, 14, 14, 16);  // opaque, so text can use ClearType
    static readonly Color Edge = Color.FromArgb(30, 255, 255, 255);  // hairline, so it reads on dark backgrounds
    static readonly Color Ink = Color.FromArgb(242, 242, 247);
    static readonly Color Muted = Color.FromArgb(160, 160, 168);
    static readonly Color Track = Color.FromArgb(46, 46, 52);
    static readonly Color HotFill = Color.FromArgb(18, 255, 255, 255);
    static readonly Color Green = Theme.Soon;
    static readonly Color Orange = Color.FromArgb(255, 159, 10);

    const double OpenAfterMs = 650;   // how long the mouse rests on the pill before it opens
    const double CloseAfterMs = 300;  // how long the mouse is off the card before it closes
    const float GhostOpacity = 0.12f;

    public event Action<Point> Moved;  // the pill's new top-center point
    public event Action<int> DockMoved;  // in the taskbar: the pill's center, in pixels from the taskbar's right end
    public event Action<Reset> StartSession;
    public event Action<Reset, DateTime?> UndoSession;  // puts back the session end from before Start
    public event Action<Reset> AdjustSession;
    public event Action EditResets;

    readonly ContextMenuStrip menu;
    readonly float scale;
    readonly Timer anim = new Timer { Interval = 15 };
    readonly Timer watch = new Timer { Interval = 150 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<KeyValuePair<RectangleF, Action>> hits = new List<KeyValuePair<RectangleF, Action>>();
    readonly Font nameFont, countFont, smallFont, pillFont, pillCountFont;

    List<Reset> resets = new List<Reset>();
    Bitmap buffer;
    Point anchor;  // top-center of the collapsed pill
    bool growDown = true;
    int winW, winH, margin, pillH, cardW, cardH;
    double value, from, target;  // 0 = collapsed, 1 = open
    double animStart, animLength;
    bool opening, mouseDown;
    Point downAt;
    string lastPill;
    RectangleF shape;     // where the island was last drawn, in window coordinates
    RectangleF? hot;      // the clickable part under the mouse

    // Getting out of the way
    bool onPill, clickedThrough, fullScreen, clickThrough;
    Point restAt;
    double restSince, offCardSince = -1;
    float ghost, ghostTarget;  // 0 = solid, 1 = faded away
    byte shownAlpha = 255;

    // In the taskbar
    bool docked, taskbarHidden, lightTaskbar, hoverPill, dragging;
    Rectangle taskbar;
    int dockFromRight = -1, grabDX, pillW;
    double lastTopmost;
    float pillCX, pillY, cardY;  // window coordinates of the pill's center, its top and the card's top
    bool clearType = true;
    readonly WinEventProc foregroundHook;

    // Undo for Start
    Reset undoFor;
    DateTime? undoPrevious;
    double undoUntil;

    public Island(ContextMenuStrip menu)
    {
        this.menu = menu;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Reset Timer";
        using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
        nameFont = new Font("Segoe UI Semibold", S(14), GraphicsUnit.Pixel);
        countFont = new Font("Segoe UI Semibold", S(14), GraphicsUnit.Pixel);
        smallFont = new Font("Segoe UI", S(12), GraphicsUnit.Pixel);
        pillFont = new Font("Segoe UI", S(13), GraphicsUnit.Pixel);
        pillCountFont = new Font("Segoe UI Semibold", S(13), GraphicsUnit.Pixel);
        margin = S(18);
        pillH = S(30);
        cardW = S(320);
        anim.Tick += (s, e) => Step();
        watch.Tick += (s, e) => Watch();
        VisibleChanged += (s, e) => { if (Visible) watch.Start(); else watch.Stop(); };
        // Clicking the taskbar raises it over every other always-on-top window, so step back in front.
        foregroundHook = (h, ev, hwnd, o, c, t, time) => { if (docked && Visible) KeepOnTop(); };
        SetWinEventHook(3, 3, IntPtr.Zero, foregroundHook, 0, 0, 0);  // EVENT_SYSTEM_FOREGROUND, out of context
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            // WS_EX_LAYERED | WS_EX_TOOLWINDOW (no Alt+Tab) | WS_EX_NOACTIVATE | WS_EX_TOPMOST
            cp.ExStyle |= 0x80000 | 0x80 | 0x08000000 | 0x8;
            return cp;
        }
    }

    int S(float px) { return (int)Math.Round(px * scale); }
    double Ms { get { return clock.Elapsed.TotalMilliseconds; } }

    // ---- placement ----

    public void ShowAt(Point? saved)
    {
        docked = false;
        anchor = saved.HasValue && OnScreen(saved.Value) ? saved.Value : DefaultAnchor();
        Open();
    }

    // In the taskbar; fromRight < 0 picks the empty space just left of the notification area. A
    // taskbar on the left or right of the screen has no room, so it floats instead.
    public void ShowDocked(int fromRight)
    {
        dockFromRight = fromRight;
        docked = RefreshTaskbar() && taskbar.Width > taskbar.Height;
        if (!docked) anchor = DefaultAnchor();
        lightTaskbar = LightTaskbar();
        Open();
    }

    void Open()
    {
        anim.Stop();
        value = target = 0;
        onPill = hoverPill = dragging = false;
        ghost = ghostTarget = 0;
        lastPill = null;
        Measure();
        Place();
        Show();
        Render();
        Apply();
        if (docked) KeepOnTop();
    }

    void KeepOnTop()
    {
        lastTopmost = Ms;
        SetWindowPos(Handle, (IntPtr)(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);  // HWND_TOPMOST; no size, move or activate
    }

    // Reads where the taskbar is and whether it's slid away (auto-hide). Returns false if there's none.
    bool RefreshTaskbar()
    {
        RECT r;
        IntPtr tb = FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero || !GetWindowRect(tb, out r)) return false;
        var rect = Rectangle.FromLTRB(r.L, r.T, r.R, r.B);
        Rectangle seen = Rectangle.Intersect(rect, Screen.FromRectangle(rect).Bounds);
        taskbarHidden = Math.Min(seen.Width, seen.Height) < Math.Min(rect.Width, rect.Height) / 2;
        if (rect != taskbar)
        {
            taskbar = rect;
            if (docked && Visible) { Place(); Render(); }
        }
        return true;
    }

    static bool LightTaskbar()
    {
        try
        {
            object v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
            return v is int && (int)v == 1;
        }
        catch (Exception) { return false; }
    }

    // Where the pill goes by default: the empty space just left of the clock and tray icons.
    int DefaultFromRight()
    {
        RECT r;
        IntPtr tray = FindWindowEx(FindWindow("Shell_TrayWnd", null), IntPtr.Zero, "TrayNotifyWnd", null);
        int trayLeft = tray != IntPtr.Zero && GetWindowRect(tray, out r) ? r.L : taskbar.Right - S(300);
        return taskbar.Right - trayLeft + S(16) + pillW / 2;
    }

    Point DefaultAnchor()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        return new Point(wa.Left + wa.Width / 2, wa.Top + S(10));
    }

    static bool OnScreen(Point p)
    {
        foreach (Screen s in Screen.AllScreens)
            if (s.WorkingArea.Contains(p)) return true;
        return false;  // saved spot is off-screen (monitor unplugged)
    }

    public void SetResets(List<Reset> list)
    {
        resets = list;
        lastPill = null;
        if (!Visible) return;
        Measure();
        Place();
        Render();
    }

    void Measure()
    {
        using (var bmp = new Bitmap(1, 1))
        using (Graphics g = Graphics.FromImage(bmp))
            pillW = PillContent(g, DateTime.Now).Width;
        cardH = DrawCard(null, 0, 0, 1, DateTime.Now, false);
        winW = cardW + margin * 2;
        winH = cardH + margin * 2;
    }

    void Place()
    {
        if (docked) { PlaceDocked(); return; }
        Rectangle wa = Screen.FromPoint(anchor).WorkingArea;
        growDown = anchor.Y < wa.Top + wa.Height / 2;
        winH = cardH + margin * 2;
        pillCX = winW / 2f;
        pillY = growDown ? margin : winH - margin - pillH;
        cardY = growDown ? margin : winH - margin - cardH;
        int top = growDown ? anchor.Y - margin : anchor.Y + pillH + margin - winH;
        SetBounds(anchor.X - winW / 2, top, winW, winH);
    }

    // The pill is centered in the taskbar's height; the card opens just outside the taskbar, kept on
    // screen even when the pill is near the end of the taskbar.
    void PlaceDocked()
    {
        Rectangle tb = taskbar, screen = Screen.FromRectangle(tb).Bounds;
        if (dockFromRight < 0) dockFromRight = DefaultFromRight();
        int half = pillW / 2 + S(6);
        int cx = Math.Max(tb.Left + half, Math.Min(tb.Right - half, tb.Right - dockFromRight));
        bool bottom = tb.Top > screen.Top + screen.Height / 2;
        growDown = !bottom;
        int pillTop = tb.Top + (tb.Height - pillH) / 2;
        int cardTop = bottom ? tb.Top - S(12) - cardH : tb.Bottom + S(12);
        int winTop = (bottom ? cardTop : pillTop) - margin;
        winH = (bottom ? pillTop + pillH : cardTop + cardH) + margin - winTop;
        int winLeft = Math.Max(screen.Left - margin + S(12), Math.Min(screen.Right + margin - S(12) - winW, cx - winW / 2));
        pillCX = cx - winLeft;
        pillY = pillTop - winTop;
        cardY = cardTop - winTop;
        SetBounds(winLeft, winTop, winW, winH);
    }

    // ---- open / close animation ----

    void Animate(bool open)
    {
        double to = open ? 1 : 0;
        if (to == target) return;
        from = value;
        target = to;
        opening = open;
        hot = null;
        animStart = Ms;
        animLength = open ? 420 : 260;
        anim.Start();
        Step();
    }

    void Step()
    {
        double p = Math.Min(1, (Ms - animStart) / animLength);
        double eased = opening ? EaseOutBack(p) : EaseOutCubic(p);
        value = from + (target - from) * eased;
        if (p >= 1)
        {
            value = target;
            anim.Stop();
        }
        Render();
    }

    // Overshoots by about 4% before settling: the "spring" in the opening animation.
    static double EaseOutBack(double p)
    {
        const double c1 = 1.05, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(p - 1, 3) + c1 * Math.Pow(p - 1, 2);
    }

    static double EaseOutCubic(double p) { return 1 - Math.Pow(1 - p, 3); }

    public void UpdateTimes(DateTime now)
    {
        if (!Visible) return;
        CheckFullScreen();
        if (docked)
        {
            bool light = LightTaskbar();
            if (light != lightTaskbar) { lightTaskbar = light; lastPill = null; }
        }
        if (anim.Enabled || Gone) return;
        if (DrawCard(null, 0, 0, 1, now, false) != cardH) { Remeasure(); return; }  // a session started or ended
        if (value > 0 || PillKey(now) != lastPill) Render();
    }

    // For when the card's rows change height, such as a session starting.
    void Remeasure()
    {
        Measure();
        Place();
        Render();
    }

    // ---- getting out of the way ----

    void CheckFullScreen()
    {
        int state;
        // 2 = a full-screen app, 3 = a full-screen Direct3D game, 4 = presentation mode
        bool now = SHQueryUserNotificationState(out state) == 0 && state >= 2 && state <= 4;
        if (now == fullScreen) return;
        fullScreen = now;
        if (fullScreen) { anim.Stop(); value = target = 0; onPill = false; ghost = ghostTarget = 0; Render(); }
        Apply();
    }

    // Runs a few times a second; quicker while the mouse is on the island or it's fading.
    void Watch()
    {
        if (fullScreen) return;
        Point c = Cursor.Position;
        double now = Ms;

        if (docked)
        {
            bool wasHidden = taskbarHidden;
            RefreshTaskbar();
            if (taskbarHidden != wasHidden) Apply();
            if (Gone) return;
            if (now - lastTopmost > 500) KeepOnTop();
        }

        if (docked && target == 0 && !anim.Enabled)
        {
            // Nothing is under it here, so no fading: hover to open, or click.
            RectangleF pill = ToScreen(PillRect());
            bool inside = pill.Contains(c);
            if (inside != hoverPill) { hoverPill = inside; Render(); }
            if (inside && !onPill) { onPill = true; restAt = c; restSince = now; }
            else if (!inside) onPill = false;
            if (onPill && !dragging && !mouseDown)
            {
                if (Math.Abs(c.X - restAt.X) > S(4) || Math.Abs(c.Y - restAt.Y) > S(4)) { restAt = c; restSince = now; }
                if (now - restSince >= 300) { onPill = hoverPill = false; offCardSince = -1; Animate(true); return; }
            }
        }
        else if (target == 0 && !anim.Enabled)
        {
            RectangleF pill = ToScreen(shape);
            pill.Inflate(S(3), S(3));
            if (pill.Contains(c))
            {
                if (!onPill)
                {
                    onPill = true;
                    clickedThrough = false;
                    restAt = c;
                    restSince = now;
                }
                if (GetAsyncKeyState(0x01) < 0 || GetAsyncKeyState(0x02) < 0) clickedThrough = true;
                if (Math.Abs(c.X - restAt.X) > S(4) || Math.Abs(c.Y - restAt.Y) > S(4))
                {
                    restAt = c;
                    restSince = now;
                }
                double rest = now - restSince;
                if (clickedThrough) ghostTarget = 1;
                else if (rest >= OpenAfterMs)
                {
                    onPill = false;
                    ghost = ghostTarget = 0;
                    offCardSince = -1;
                    Apply();
                    Animate(true);
                    return;
                }
                // Fades away at once, then back in while the mouse rests: the cue that it's about to open.
                else ghostTarget = rest < 180 ? 1 : (float)(1 - (rest - 180) / (OpenAfterMs - 180));
            }
            else
            {
                onPill = false;
                ghostTarget = 0;
            }
        }
        else if (target == 1)
        {
            // The card and the pill, and the gap between them in the taskbar.
            RectangleF card = ToScreen(RectangleF.Union(CardRect(), PillRect()));
            card.Inflate(S(8), S(8));
            bool busy = mouseDown || menu.Visible || Form.ActiveForm is SessionDialog;
            if (card.Contains(c) || busy) offCardSince = -1;
            else if (offCardSince < 0) offCardSince = now;
            else if (now - offCardSince >= CloseAfterMs && !anim.Enabled)
            {
                offCardSince = -1;
                Animate(false);
            }
        }

        // Ease the fade toward its target.
        if (Math.Abs(ghostTarget - ghost) > 0.01f) ghost += (ghostTarget - ghost) * 0.4f;
        else ghost = ghostTarget;
        Apply();

        int interval = onPill || target == 1 || ghost != ghostTarget ? 30 : 150;
        if (watch.Interval != interval) watch.Interval = interval;
    }

    // Applies the fade and click-through for the current state.
    void Apply()
    {
        bool through = Gone || ghost > 0.02f || ghostTarget > 0;
        if (through != clickThrough)
        {
            clickThrough = through;
            int ex = GetWindowLong(Handle, GWL_EXSTYLE);
            SetWindowLong(Handle, GWL_EXSTYLE, through ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        }
        byte alpha = Gone ? (byte)0 : (byte)Math.Round(255 * (1 - (1 - GhostOpacity) * ghost));
        if (alpha != shownAlpha)
        {
            shownAlpha = alpha;
            Push();
        }
    }

    RectangleF ToScreen(RectangleF r) { r.Offset(Left, Top); return r; }

    // Hidden: behind a full-screen app, or in an auto-hidden taskbar that has slid away.
    bool Gone { get { return fullScreen || (docked && taskbarHidden && target == 0 && !anim.Enabled); } }

    RectangleF CardRect() { return new RectangleF(winW / 2f - cardW / 2f, cardY, cardW, cardH); }
    RectangleF PillRect() { return new RectangleF(pillCX - pillW / 2f, pillY, pillW, pillH); }

    static RectangleF LerpRect(RectangleF a, RectangleF b, float t)
    {
        float x = Lerp(a.X, b.X, t), y = Lerp(a.Y, b.Y, t);
        float w = Lerp(a.Width, b.Width, t), h = Lerp(a.Height, b.Height, t);
        return new RectangleF((float)Math.Round(x), (float)Math.Round(y), (float)Math.Round(w), (float)Math.Round(h));
    }

    static Color LerpColor(Color a, Color b, float t)
    {
        t = Clamp01(t);
        return Color.FromArgb((int)Lerp(a.A, b.A, t), (int)Lerp(a.R, b.R, t), (int)Lerp(a.G, b.G, t), (int)Lerp(a.B, b.B, t));
    }

    // ---- drawing ----

    void Render()
    {
        if (!Visible || winW <= 0) return;
        if (buffer == null || buffer.Width != winW || buffer.Height != winH)
        {
            if (buffer != null) buffer.Dispose();
            buffer = new Bitmap(winW, winH, PixelFormat.Format32bppArgb);
        }
        DateTime now = DateTime.Now;
        float v = (float)value;

        using (Graphics g = Graphics.FromImage(buffer))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            Pill pill = PillContent(g, now);
            pillW = pill.Width;
            shape = LerpRect(PillRect(), CardRect(), v);
            float h = shape.Height;
            float radius = Math.Min(Lerp(docked ? S(6) : pillH / 2f, S(22), Clamp01(v)), h / 2);

            // In the taskbar the collapsed pill has no background of its own, only a hover highlight
            // like a taskbar button's. (Alpha 1, not 0, so it still catches the mouse.)
            float solid = docked ? Clamp01(v * 2) : 1;
            Color pillBack = !hoverPill ? Color.FromArgb(1, 0, 0, 0)
                : lightTaskbar ? Color.FromArgb(22, 0, 0, 0) : Color.FromArgb(24, 255, 255, 255);
            Color fill = docked ? LerpColor(pillBack, Fill, solid) : Fill;
            clearType = fill.A == 255;

            if (solid > 0)
                for (int i = 3; i >= 1; i--)  // soft shadow, so it reads on light backgrounds too
                    using (GraphicsPath p = Rounded(Grow(shape, i * S(2)), radius + i * S(2)))
                    using (var b = new SolidBrush(Color.FromArgb((int)((14 + 6 * (3 - i)) * solid), 0, 0, 0)))
                        g.FillPath(b, p);

            using (GraphicsPath path = Rounded(shape, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                g.SetClip(path);
                hits.Clear();
                float pillAlpha = Clamp01(1 - v * 3);
                float cardAlpha = Clamp01((v - 0.3f) / 0.6f);
                if (pillAlpha > 0) DrawPill(g, pill, pillAlpha);
                if (cardAlpha > 0)
                {
                    float cardTop = growDown ? shape.Y : shape.Bottom - cardH;
                    float slide = (float)Math.Round((1 - cardAlpha) * S(8) * (growDown ? -1 : 1));
                    DrawCard(g, (float)Math.Round(shape.X + shape.Width / 2 - cardW / 2f), cardTop + slide, cardAlpha, now, !anim.Enabled && value >= 1);
                }
                g.ResetClip();
            }
            using (GraphicsPath edge = Rounded(Grow(shape, -0.5f), radius - 0.5f))
            using (var pen = new Pen(Color.FromArgb((int)(Edge.A * solid), Edge), 1))
                g.DrawPath(pen, edge);
        }
        Push();
    }

    void Push()
    {
        if (buffer == null || !IsHandleCreated) return;
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
        IntPtr bmp = buffer.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, bmp);
        var dst = new POINT { X = Left, Y = Top };
        var size = new SIZE { CX = winW, CY = winH };
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = shownAlpha, AlphaFormat = 1 };  // AC_SRC_ALPHA
        UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2);  // ULW_ALPHA
        SelectObject(mem, old);
        DeleteObject(bmp);
        DeleteDC(mem);
        ReleaseDC(IntPtr.Zero, screen);
    }

    class Pill
    {
        public string Name, Count;
        public double Progress;
        public Color Color;
        public int NameW, CountW, Width;
    }

    // What the collapsed pill shows: the soonest running session, or else the soonest weekly reset.
    // Kept short ("2d 21h", "4h 05m") so the pill stays small; the card has the full countdowns.
    Pill PillContent(Graphics g, DateTime now)
    {
        Reset best = null;
        TimeSpan bestLeft = TimeSpan.MaxValue;
        foreach (Reset r in resets)
            if (r.SessionEnd.HasValue && r.SessionEnd.Value - now < bestLeft) { best = r; bestLeft = r.SessionEnd.Value - now; }
        bool session = best != null;
        if (!session)
            foreach (Reset r in resets)
                if (r.Next(now) - now < bestLeft) { best = r; bestLeft = r.Next(now) - now; }

        var p = new Pill();
        if (best == null)
        {
            p.Name = "No resets yet";
            p.Count = "";
            p.Color = Muted;
        }
        else
        {
            if (bestLeft < TimeSpan.Zero) bestLeft = TimeSpan.Zero;
            p.Name = best.Name;
            p.Count = Compact(bestLeft);
            if (session)
            {
                p.Progress = 1 - bestLeft.TotalHours / best.SessionHours;
                p.Color = Orange;
            }
            else
            {
                p.Progress = 1 - bestLeft.TotalDays / 7;
                p.Color = bestLeft.TotalHours < 24 ? Green : Ink;
            }
        }
        if (g != null)
        {
            p.NameW = Math.Min(TextWidth(g, p.Name, pillFont), S(120));
            p.CountW = p.Count.Length == 0 ? 0 : TextWidth(g, p.Count, pillCountFont);
            p.Width = S(9) + S(16) + S(8) + p.NameW + (p.CountW > 0 ? S(8) + p.CountW : 0) + S(14);
        }
        return p;
    }

    static string Compact(TimeSpan t)
    {
        if (t.TotalDays >= 1) return string.Format("{0}d {1}h", t.Days, t.Hours);
        if (t.TotalHours >= 1) return string.Format("{0}h {1:00}m", (int)t.TotalHours, t.Minutes);
        return string.Format("{0}m", t.Minutes);
    }

    // Changes only when the pill would look different, so a collapsed pill rarely redraws.
    string PillKey(DateTime now)
    {
        Pill p = PillContent(null, now);
        return p.Name + p.Count + p.Color.ToArgb() + (int)(p.Progress * 100);
    }

    void DrawPill(Graphics g, Pill p, float a)
    {
        lastPill = p.Name + p.Count + p.Color.ToArgb() + (int)(p.Progress * 100);
        float x = (float)Math.Round(shape.X + (shape.Width - p.Width) / 2), y = growDown ? shape.Y : shape.Bottom - pillH;
        int d = S(16);
        // On a light taskbar, darker versions of the same colors.
        bool light = docked && lightTaskbar;
        Color count = !light ? p.Color
            : p.Color == Green ? Theme.SoonOnLight : p.Color == Orange ? Color.FromArgb(190, 100, 0) : Color.FromArgb(24, 24, 28);
        Color ring = p.Color == Ink ? (light ? Theme.SoonOnLight : Green) : count;
        Ring(g, x + S(9) + d / 2f, y + pillH / 2f, d, p.Progress, ring, a, light ? Color.FromArgb(205, 205, 210) : Track);
        float textX = x + S(9) + d + S(8);
        DrawText(g, p.Name, pillFont, light ? Color.FromArgb(80, 80, 88) : Muted, a, new RectangleF(textX, y, p.NameW + S(2), pillH), StringAlignment.Near);
        if (p.CountW > 0)
            DrawText(g, p.Count, pillCountFont, count, a, new RectangleF(textX + p.NameW + S(8), y, p.CountW + S(2), pillH), StringAlignment.Near);
    }

    bool IsHot(RectangleF r) { return hot.HasValue && hot.Value == r; }

    void HotBackground(Graphics g, RectangleF r, float a)
    {
        if (!IsHot(r)) return;
        using (GraphicsPath p = Rounded(r, S(8)))
        using (var b = new SolidBrush(Color.FromArgb((int)(HotFill.A * a), HotFill)))
            g.FillPath(b, p);
    }

    // Draws the open card with its top-left at (x0, y0) and returns its height. With g == null it
    // only measures; with record set, it also notes where the clickable parts are.
    int DrawCard(Graphics g, float x0, float y0, float a, DateTime now, bool record)
    {
        int pad = S(18), w = cardW - pad * 2;
        float x = x0 + pad, y = y0 + pad;

        if (resets.Count == 0)
        {
            if (g != null) DrawText(g, "No resets yet", countFont, Muted, a, new RectangleF(x, y, w, S(20)), StringAlignment.Near);
            y += S(20);
        }
        for (int i = 0; i < resets.Count; i++)
        {
            Reset r = resets[i];
            if (i > 0) y += S(18);
            TimeSpan left = r.Next(now) - now;
            bool soon = left.TotalHours < 24;
            if (g != null)
            {
                DrawText(g, r.Name, nameFont, Ink, a, new RectangleF(x, y, w * 0.48f, S(20)), StringAlignment.Near);
                DrawText(g, Reset.Long(left), countFont, soon ? Green : Ink, a, new RectangleF(x + w * 0.4f, y, w * 0.6f, S(20)), StringAlignment.Far);
                Bar(g, x, y + S(27), w, 1 - left.TotalDays / 7, soon ? Green : Color.FromArgb(200, Ink), a);
                DrawText(g, "Resets " + r.Schedule, smallFont, Muted, a, new RectangleF(x, y + S(35), w, S(16)), StringAlignment.Near);
            }
            y += S(51);

            if (r.SessionHours == 0) continue;
            y += S(12);
            Reset target = r;
            if (r.SessionEnd.HasValue)
            {
                TimeSpan s = r.SessionEnd.Value - now;
                bool undo = undoFor == r && Ms < undoUntil;  // just started from the card
                var row = new RectangleF(x - S(8), y - S(7), w + S(16), S(62));
                var undoButton = new RectangleF(x + w - S(56), y + S(32), S(56), S(22));
                if (g != null)
                {
                    HotBackground(g, row, a);
                    DrawText(g, r.SessionLabel, smallFont, IsHot(row) ? Ink : Muted, a, new RectangleF(x, y, w * 0.5f, S(20)), StringAlignment.Near);
                    DrawText(g, Reset.Hms(s), countFont, Orange, a, new RectangleF(x + w * 0.4f, y, w * 0.6f, S(20)), StringAlignment.Far);
                    Bar(g, x, y + S(27), w, 1 - s.TotalHours / r.SessionHours, Orange, a);
                    DrawText(g, (undo ? "Started. Resets at " : "Resets at ") + Reset.Clock(r.SessionEnd.Value.TimeOfDay),
                        smallFont, Muted, a, new RectangleF(x, y + S(35), w - S(64), S(16)), StringAlignment.Near);
                    if (undo)
                    {
                        using (GraphicsPath p = Rounded(undoButton, undoButton.Height / 2))
                        using (var b = new SolidBrush(Color.FromArgb((int)((IsHot(undoButton) ? 56 : 28) * a), 255, 255, 255)))
                            g.FillPath(b, p);
                        DrawText(g, "Undo", smallFont, Ink, a, undoButton, StringAlignment.Center);
                    }
                }
                if (record)
                {
                    if (undo) hits.Add(Hit(undoButton, () =>
                    {
                        undoFor = null;
                        if (UndoSession != null) UndoSession(target, undoPrevious);
                        Remeasure();
                    }));
                    hits.Add(Hit(row, () => { if (AdjustSession != null) AdjustSession(target); }));
                }
                y += S(51);
            }
            else
            {
                var button = new RectangleF(x + w - S(64), y - S(2), S(64), S(26));
                if (g != null)
                {
                    DrawText(g, r.SessionLabel + " not started", smallFont, Muted, a, new RectangleF(x, y, w - S(72), S(22)), StringAlignment.Near);
                    using (GraphicsPath p = Rounded(button, button.Height / 2))
                    using (var b = new SolidBrush(Color.FromArgb((int)((IsHot(button) ? 90 : 46) * a), Orange)))
                        g.FillPath(b, p);
                    DrawText(g, "Start", smallFont, IsHot(button) ? Color.FromArgb(255, 214, 160) : Orange, a, button, StringAlignment.Center);
                }
                if (record) hits.Add(Hit(button, () =>
                {
                    // One click starts a 5-hour window, so offer to take it back for a few seconds.
                    undoFor = target;
                    undoPrevious = target.SessionEnd;
                    undoUntil = Ms + 8000;
                    if (StartSession != null) StartSession(target);
                    Remeasure();
                }));
                y += S(22);
            }
        }

        y += S(16);
        if (g != null)
            using (var pen = new Pen(Color.FromArgb((int)(255 * a), Track), Math.Max(1, S(1))))
                g.DrawLine(pen, x, y, x + w, y);
        y += S(8);
        var edit = new RectangleF(x - S(8), y, S(100), S(28));
        var more = new RectangleF(x + w - S(28), y, S(36), S(28));
        if (g != null)
        {
            HotBackground(g, edit, a);
            HotBackground(g, more, a);
            DrawText(g, "Edit resets", smallFont, IsHot(edit) ? Ink : Muted, a, new RectangleF(x, y, S(90), S(28)), StringAlignment.Near);
            using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), IsHot(more) ? Ink : Muted)))  // "more" dots
                for (int i = 0; i < 3; i++) g.FillEllipse(b, more.X + S(8) + i * S(7), y + S(12.5f), S(3.5f), S(3.5f));
        }
        if (record)
        {
            hits.Add(Hit(edit, () => { if (EditResets != null) EditResets(); }));
            hits.Add(Hit(more, ShowMenu));
        }
        y += S(28) + pad - S(8);
        return (int)Math.Ceiling(y - y0);
    }

    static KeyValuePair<RectangleF, Action> Hit(RectangleF r, Action act) { return new KeyValuePair<RectangleF, Action>(r, act); }

    void Bar(Graphics g, float x, float y, float w, double fraction, Color color, float a)
    {
        float h = S(4);
        fraction = Math.Max(0, Math.Min(1, fraction));
        using (GraphicsPath p = Rounded(new RectangleF(x, y, w, h), h / 2))
        using (var b = new SolidBrush(Color.FromArgb((int)(255 * a), Track)))
            g.FillPath(b, p);
        float fw = (float)(w * fraction);
        if (fw < h) return;
        using (GraphicsPath p = Rounded(new RectangleF(x, y, fw, h), h / 2))
        using (var b = new SolidBrush(Color.FromArgb((int)(color.A * a), color)))
            g.FillPath(b, p);
    }

    void Ring(Graphics g, float cx, float cy, int d, double fraction, Color color, float a, Color trackColor)
    {
        fraction = Math.Max(0, Math.Min(1, fraction));
        float stroke = S(2.5f), r = d / 2f - stroke / 2;
        var box = new RectangleF(cx - r, cy - r, r * 2, r * 2);
        using (var track = new Pen(Color.FromArgb((int)(255 * a), trackColor), stroke))
            g.DrawEllipse(track, box);
        if (fraction <= 0) return;
        using (var pen = new Pen(Color.FromArgb((int)(255 * a), color), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(pen, box, -90, (float)(360 * fraction));
    }

    // Text is always drawn on the opaque fill, so it can use ClearType once it's fully faded in. It's
    // placed on whole pixels, without the half-pixel shift used for the shapes, so it stays sharp.
    void DrawText(Graphics g, string s, Font f, Color c, float a, RectangleF rect, StringAlignment align)
    {
        PixelOffsetMode offset = g.PixelOffsetMode;
        TextRenderingHint hint = g.TextRenderingHint;
        g.PixelOffsetMode = PixelOffsetMode.None;
        g.TextRenderingHint = a >= 1 && clearType ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;
        // Center vertically by hand, on a whole pixel, instead of letting GDI+ land on a half one.
        float lineH = f.GetHeight(g);
        var snapped = new RectangleF((float)Math.Round(rect.X), (float)Math.Round(rect.Y + (rect.Height - lineH) / 2),
            (float)Math.Round(rect.Width), (float)Math.Ceiling(lineH));
        using (var b = new SolidBrush(Color.FromArgb((int)(c.A * a), c)))
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
        {
            sf.Alignment = align;
            sf.LineAlignment = StringAlignment.Near;
            sf.Trimming = StringTrimming.EllipsisCharacter;
            sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
            g.DrawString(s, f, b, snapped, sf);
        }
        g.PixelOffsetMode = offset;
        g.TextRenderingHint = hint;
    }

    static int TextWidth(Graphics g, string s, Font f)
    {
        using (var sf = new StringFormat(StringFormat.GenericTypographic))
            return (int)Math.Ceiling(g.MeasureString(s, f, 10000, sf).Width);
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static RectangleF Grow(RectangleF r, float by) { r.Inflate(by, by); return r; }
    static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    static float Clamp01(float v) { return Math.Max(0, Math.Min(1, v)); }

    // ---- mouse (only reaches the open card; the collapsed pill lets clicks through) ----

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        mouseDown = true;
        downAt = e.Location;
        grabDX = e.X - (int)pillCX;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (docked && (dragging || (mouseDown && target == 0 && Math.Abs(e.X - downAt.X) > S(4))))
        {
            // Slides along the taskbar only.
            dragging = true;
            dockFromRight = taskbar.Right - (Cursor.Position.X - grabDX);
            Place();
            Render();
            return;
        }
        if (docked && target == 0) { Cursor = Cursors.Hand; return; }
        if (mouseDown && (Math.Abs(e.X - downAt.X) > S(4) || Math.Abs(e.Y - downAt.Y) > S(4)))
        {
            mouseDown = false;
            hot = null;
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);  // WM_NCLBUTTONDOWN on HTCAPTION; returns when drag ends
            anchor = new Point(Left + winW / 2, growDown ? Top + margin : Top + winH - margin - pillH);
            Place();
            Render();
            if (Moved != null) Moved(anchor);
            return;
        }
        RectangleF? now = HitRectAt(e.Location);
        Cursor = now.HasValue ? Cursors.Hand : Cursors.SizeAll;
        if (now != hot)
        {
            hot = now;
            if (!anim.Enabled) Render();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (hot.HasValue) { hot = null; if (!anim.Enabled) Render(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { ShowMenu(); return; }
        if (dragging)
        {
            dragging = mouseDown = false;
            int half = pillW / 2 + S(6);
            dockFromRight = Math.Max(half, Math.Min(taskbar.Width - half, dockFromRight));
            if (DockMoved != null) DockMoved(dockFromRight);
            return;
        }
        if (!mouseDown) return;
        mouseDown = false;
        if (docked && target == 0) { onPill = false; Animate(true); return; }
        foreach (var h in hits)
            if (h.Key.Contains(e.Location)) { h.Value(); return; }
    }

    RectangleF? HitRectAt(Point p)
    {
        foreach (var h in hits)
            if (h.Key.Contains(p)) return h.Key;
        return null;
    }

    // The island never takes focus, so a menu shown from it never hears that you clicked elsewhere and
    // stays open. Bringing the menu itself to the front fixes that: clicking anywhere else now closes it.
    void ShowMenu()
    {
        menu.Show(Cursor.Position);
        SetForegroundWindow(menu.Handle);
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
}
