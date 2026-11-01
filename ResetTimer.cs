// Tray app (plus optional always-on-top widget) that counts down to weekly AI usage resets.
// Build: build.bat (uses the C# compiler that ships with Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main()
    {
        bool first;
        using (new System.Threading.Mutex(true, "ResetTimer.SingleInstance", out first))
        {
            if (!first) return;
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp());
        }
    }
}

static class Theme
{
    public static readonly Color Bg = Color.FromArgb(28, 28, 30);
    public static readonly Color Fg = Color.FromArgb(235, 235, 235);
    public static readonly Color Dim = Color.FromArgb(140, 140, 145);
    public static readonly Color Soon = Color.FromArgb(120, 200, 150);
    public static readonly Color SoonOnLight = Color.FromArgb(25, 125, 65);
    public static readonly Color Warn = Color.FromArgb(230, 160, 90);
}

class Reset
{
    public string Name;
    public DayOfWeek Day;
    public TimeSpan Time;
    public int SessionHours;      // rolling limit (e.g. Claude's 5-hour window); 0 = none
    public DateTime? SessionEnd;  // when the current session window resets; null = not started
    public DateTime LastNext;     // runtime only: lets Tick spot the moment a weekly reset passes

    public string SessionLabel { get { return SessionHours + "-hour limit"; } }

    public DateTime Next(DateTime now)
    {
        int days = ((int)Day - (int)now.DayOfWeek + 7) % 7;
        DateTime t = now.Date.AddDays(days).Add(Time);
        return t <= now ? t.AddDays(7) : t;
    }

    // Display only; resets.txt always stores 24-hour times.
    public static bool Use12h;

    public static string Clock(TimeSpan t)
    {
        return DateTime.Today.Add(t).ToString(Use12h ? "h:mm tt" : "HH:mm", CultureInfo.InvariantCulture);
    }

    public string Schedule
    {
        get { return Day.ToString().Substring(0, 3) + " " + Clock(Time); }
    }

    public Reset Copy()
    {
        return new Reset { Name = Name, Day = Day, Time = Time, SessionHours = SessionHours, SessionEnd = SessionEnd };
    }

    public override string ToString()
    {
        return Name + "   (" + Schedule + (SessionHours > 0 ? ", " + SessionHours + "h" : "") + ")";
    }

    public static string Long(TimeSpan t) { return string.Format("{0}d {1:00}h {2:00}m {3:00}s", t.Days, t.Hours, t.Minutes, t.Seconds); }
    public static string Hms(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return string.Format("{0}h {1:00}m {2:00}s", (int)t.TotalHours, t.Minutes, t.Seconds);
    }
    public static string Short(TimeSpan t) { return string.Format("{0}d {1}h {2}m", t.Days, t.Hours, t.Minutes); }
    public static string When(DateTime d) { return d.ToString("dddd, d MMM") + " at " + Clock(d.TimeOfDay); }
}

// resets.txt holds the reset list, sessions.txt the running session windows,
// settings.txt the window/tray preferences.
static class Store
{
    static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
    public static readonly string ResetsPath = Path.Combine(Dir, "resets.txt");
    static readonly string SessionsPath = Path.Combine(Dir, "sessions.txt");
    static readonly string[] TimeFormats = { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "h tt", "htt" };
    const string StampFormat = "yyyy-MM-dd HH:mm:ss";
    const string Header =
        "# Reset Timer - edit from the tray icon: right-click > Edit resets...\r\n" +
        "# Format: Name | Day | Time (your local time) | optional session limit, e.g. 5h\r\n";

    public static List<Reset> LoadResets(List<string> errors)
    {
        if (!File.Exists(ResetsPath)) File.WriteAllText(ResetsPath, Header + "\r\nClaude | Sunday | 16:00 | 5h\r\n");
        var list = new List<Reset>();
        foreach (string raw in File.ReadAllLines(ResetsPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] parts = line.Split('|');
            DayOfWeek day;
            DateTime time;
            int hours = 0;
            if (parts.Length < 3 || parts.Length > 4 || parts[0].Trim().Length == 0
                || !TryParseDay(parts[1].Trim(), out day)
                || !DateTime.TryParseExact(parts[2].Trim().ToUpperInvariant(), TimeFormats,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
                || (parts.Length == 4 && !TryParseHours(parts[3], out hours)))
            {
                errors.Add(line);
                continue;
            }
            list.Add(new Reset { Name = parts[0].Trim(), Day = day, Time = time.TimeOfDay, SessionHours = hours });
        }
        LoadSessions(list);
        return list;
    }

    public static void SaveResets(List<Reset> resets)
    {
        var sb = new StringBuilder(Header).Append("\r\n");
        foreach (Reset r in resets)
        {
            sb.AppendFormat("{0} | {1} | {2}", CleanName(r.Name), r.Day, DateTime.Today.Add(r.Time).ToString("HH:mm"));
            if (r.SessionHours > 0) sb.Append(" | " + r.SessionHours + "h");
            sb.Append("\r\n");
        }
        File.WriteAllText(ResetsPath, sb.ToString());
    }

    static void LoadSessions(List<Reset> resets)
    {
        var ends = new Dictionary<string, DateTime>();
        try
        {
            foreach (string line in File.ReadAllLines(SessionsPath))
            {
                string[] p = line.Split('|');
                DateTime end;
                if (p.Length == 2 && DateTime.TryParseExact(p[1].Trim(), StampFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
                    ends[p[0].Trim()] = end;
            }
        }
        catch (IOException) { }  // no sessions saved yet
        foreach (Reset r in resets)
        {
            DateTime end;
            if (r.SessionHours > 0 && ends.TryGetValue(r.Name, out end)) r.SessionEnd = end;
        }
    }

    public static void SaveSessions(List<Reset> resets)
    {
        var sb = new StringBuilder();
        foreach (Reset r in resets)
            if (r.SessionHours > 0 && r.SessionEnd.HasValue)
                sb.AppendFormat("{0} | {1}\r\n", CleanName(r.Name),
                    r.SessionEnd.Value.ToString(StampFormat, CultureInfo.InvariantCulture));
        try { File.WriteAllText(SessionsPath, sb.ToString()); } catch (Exception) { }
    }

    static string CleanName(string name)
    {
        name = name.Replace('|', '/').Trim();
        return name.Length == 0 ? "Untitled" : name;
    }

    static bool TryParseHours(string text, out int hours)
    {
        return int.TryParse(text.Trim().TrimEnd('h', 'H'), out hours) && hours >= 1 && hours <= 24;
    }

    static bool TryParseDay(string text, out DayOfWeek day)
    {
        for (int i = 0; i < 7; i++)
        {
            day = (DayOfWeek)i;
            if (text.Length >= 3 && day.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return true;
        }
        day = DayOfWeek.Sunday;
        return false;
    }
}

class Settings
{
    static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
    public bool ShowWidget = true;
    public bool OnTop = true;
    public bool Notify = true;
    public bool Use12h = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("t");  // follow Windows until set
    public Point? Position;

    public static Settings Load()
    {
        var s = new Settings();
        try
        {
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string[] kv = line.Split('=');
                if (kv.Length != 2) continue;
                string v = kv[1].Trim();
                switch (kv[0].Trim())
                {
                    case "widget": s.ShowWidget = v == "1"; break;
                    case "ontop": s.OnTop = v == "1"; break;
                    case "ampm": s.Use12h = v == "1"; break;
                    case "notify": s.Notify = v == "1"; break;
                    case "position":
                        string[] xy = v.Split(',');
                        s.Position = new Point(int.Parse(xy[0]), int.Parse(xy[1]));
                        break;
                }
            }
        }
        catch (Exception) { }  // missing or damaged file: defaults
        return s;
    }

    public void Save()
    {
        string text = "widget=" + (ShowWidget ? 1 : 0) + "\r\nontop=" + (OnTop ? 1 : 0)
            + "\r\nampm=" + (Use12h ? 1 : 0) + "\r\nnotify=" + (Notify ? 1 : 0) + "\r\n";
        if (Position.HasValue) text += "position=" + Position.Value.X + "," + Position.Value.Y + "\r\n";
        try { File.WriteAllText(FilePath, text); } catch (Exception) { }
    }
}

class TrayApp : ApplicationContext
{
    const string AppName = "ResetTimer";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly NotifyIcon tray = new NotifyIcon();
    readonly ContextMenuStrip menu = new ContextMenuStrip();
    readonly ToolStripItem[] fixedItems;
    readonly ToolStripMenuItem showWidget, onTop, ampm, notify, startup;
    readonly List<ToolStripMenuItem> resetItems = new List<ToolStripMenuItem>();
    readonly List<ToolStripMenuItem> sessionItems = new List<ToolStripMenuItem>();  // null where no session limit
    readonly Timer timer = new Timer();
    readonly Settings settings = Settings.Load();
    readonly Icon icon = MakeIcon();
    readonly Widget widget;
    List<Reset> resets = new List<Reset>();
    DateTime configStamp;
    Editor editor;
    string lastTip;

    public TrayApp()
    {
        widget = new Widget(menu);
        widget.TopMost = settings.OnTop;
        widget.Moved += () => { settings.Position = widget.Location; settings.Save(); };
        widget.HideRequested += () => SetWidgetVisible(false);

        showWidget = new ToolStripMenuItem("Show widget", null, (s, e) => SetWidgetVisible(!widget.Visible));
        onTop = new ToolStripMenuItem("Widget always on top", null, (s, e) =>
        {
            settings.OnTop = widget.TopMost = !widget.TopMost;
            settings.Save();
        });
        Reset.Use12h = settings.Use12h;
        ampm = new ToolStripMenuItem("12-hour time (AM/PM)", null, (s, e) =>
        {
            settings.Use12h = Reset.Use12h = !Reset.Use12h;
            settings.Save();
            Reload();
            Tick();
            if (editor != null) editor.ApplyClockFormat();
        });
        notify = new ToolStripMenuItem("Notify when limits reset", null, (s, e) =>
        {
            settings.Notify = !settings.Notify;
            settings.Save();
        });
        startup = new ToolStripMenuItem("Start with Windows", null, (s, e) => SetStartup(!StartupEnabled()));
        fixedItems = new ToolStripItem[]
        {
            new ToolStripSeparator(),
            new ToolStripMenuItem("Edit resets...", null, (s, e) => OpenEditor()),
            showWidget, onTop, ampm, notify, startup,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (s, e) => Exit()),
        };
        menu.ShowItemToolTips = true;
        menu.Opening += (s, e) =>
        {
            showWidget.Checked = widget.Visible;
            onTop.Checked = widget.TopMost;
            ampm.Checked = Reset.Use12h;
            notify.Checked = settings.Notify;
            startup.Checked = StartupEnabled();
            Tick();
        };

        tray.Icon = icon;
        tray.Text = "Reset Timer";
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) SetWidgetVisible(!widget.Visible); };
        tray.Visible = true;

        Reload();
        if (settings.ShowWidget) widget.ShowAt(settings.Position);
        timer.Tick += (s, e) => Tick();
        timer.Interval = 1000;
        timer.Start();
        Tick();
    }

    void Reload()
    {
        var errors = new List<string>();
        resets = Store.LoadResets(errors);
        configStamp = File.GetLastWriteTimeUtc(Store.ResetsPath);
        widget.SetResets(resets, errors);

        menu.Items.Clear();
        resetItems.Clear();
        sessionItems.Clear();
        foreach (Reset r in resets)
        {
            var item = new ToolStripMenuItem(r.Name) { ShowShortcutKeys = true };
            item.Font = new Font(menu.Font, FontStyle.Bold);
            resetItems.Add(item);
            menu.Items.Add(item);
            ToolStripMenuItem session = r.SessionHours > 0 ? MakeSessionItem(r) : null;
            if (session != null) menu.Items.Add(session);
            sessionItems.Add(session);
        }
        if (resets.Count == 0)
            menu.Items.Add("No resets yet - add one...", null, (s, e) => OpenEditor());
        if (errors.Count > 0)
            menu.Items.Add(errors.Count + " line(s) in resets.txt couldn't be read", null,
                (s, e) => Process.Start("notepad.exe", "\"" + Store.ResetsPath + "\""));
        menu.Items.AddRange(fixedItems);
    }

    // Session windows start when you start chatting, so they're started by hand.
    // (No submenu here: Windows hides the countdown text on items that have one.)
    ToolStripMenuItem MakeSessionItem(Reset r)
    {
        return new ToolStripMenuItem("      " + r.SessionLabel, null, (s, e) => EditSession(r)) { ShowShortcutKeys = true };
    }

    void EditSession(Reset r)
    {
        using (var dialog = new SessionDialog(r, icon))
        {
            if (dialog.ShowDialog() != DialogResult.OK) return;
            r.SessionEnd = dialog.End;
            Store.SaveSessions(resets);
            Tick();
        }
    }

    void Notify(string title, string text)
    {
        if (settings.Notify) tray.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
    }

    void Tick()
    {
        try { if (File.GetLastWriteTimeUtc(Store.ResetsPath) != configStamp) Reload(); }
        catch (Exception) { }  // file mid-save; try again next tick

        DateTime now = DateTime.Now;
        var tip = new StringBuilder();
        bool sessionsChanged = false;
        for (int i = 0; i < resets.Count; i++)
        {
            Reset r = resets[i];
            DateTime next = r.Next(now);
            if (r.LastNext != DateTime.MinValue && next > r.LastNext)
                Notify(r.Name + " weekly limit has reset", "Next weekly reset: " + Reset.When(next));
            r.LastNext = next;

            if (r.SessionEnd.HasValue && now >= r.SessionEnd.Value)
            {
                // Skip stale news, e.g. a window that ended while the PC was off overnight.
                if (now - r.SessionEnd.Value < TimeSpan.FromHours(1))
                    Notify(r.Name + " " + r.SessionLabel + " has reset", "You can start a new session.");
                r.SessionEnd = null;
                sessionsChanged = true;
            }

            TimeSpan left = next - now;
            ToolStripMenuItem item = resetItems[i];
            item.ShortcutKeyDisplayString = Reset.Long(left);
            item.ForeColor = left.TotalHours < 24 ? Theme.SoonOnLight : SystemColors.MenuText;
            item.ToolTipText = "Resets " + Reset.When(next);
            item.Invalidate();
            tip.Append(r.Name).Append(": ").Append(Reset.Short(left));

            ToolStripMenuItem session = sessionItems[i];
            if (session != null)
            {
                bool running = r.SessionEnd.HasValue;
                session.ShortcutKeyDisplayString = running ? Reset.Hms(r.SessionEnd.Value - now) : "not started";
                session.ForeColor = running ? SystemColors.MenuText : SystemColors.GrayText;
                session.ToolTipText = running
                    ? "Resets at " + Reset.Clock(r.SessionEnd.Value.TimeOfDay)
                    : "Click when you start a session";
                session.Invalidate();
                TimeSpan s = r.SessionEnd.HasValue ? r.SessionEnd.Value - now : TimeSpan.Zero;
                if (running) tip.AppendFormat(" | {0}h: {1}h {2}m", r.SessionHours, (int)s.TotalHours, s.Minutes);
            }
            tip.Append('\n');
        }
        if (sessionsChanged) Store.SaveSessions(resets);
        if (widget.Visible) widget.UpdateTimes(resets, now);

        // NotifyIcon.Text throws past 63 chars on older .NET Framework builds.
        string text = resets.Count == 0 ? "Reset Timer" : tip.ToString().TrimEnd();
        if (text.Length > 63) text = text.Substring(0, 60) + "...";
        if (text != lastTip) { tray.Text = text; lastTip = text; }

        // Fire just after the next whole second so the display never skips.
        timer.Interval = 1000 - DateTime.Now.Millisecond + 5;
    }

    void SetWidgetVisible(bool visible)
    {
        if (visible)
        {
            widget.UpdateTimes(resets, DateTime.Now);
            widget.ShowAt(settings.Position);
        }
        else widget.Hide();
        settings.ShowWidget = visible;
        settings.Save();
    }

    void OpenEditor()
    {
        if (editor != null) { editor.Activate(); return; }
        editor = new Editor(resets, icon);
        editor.FormClosed += (s, e) =>
        {
            if (editor.Saved)
            {
                Store.SaveSessions(editor.Result);  // first, so renamed resets keep their running session
                Store.SaveResets(editor.Result);
                Reload();
                Tick();
            }
            editor = null;
        };
        editor.Show();
        editor.Activate();
    }

    void Exit()
    {
        timer.Stop();
        tray.Visible = false;
        tray.Dispose();
        if (editor != null) editor.Close();
        ExitThread();
    }

    static bool StartupEnabled()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
    }

    static void SetStartup(bool on)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
    }

    // Green clock face, drawn at the tray's icon size so it stays crisp at any DPI.
    static Icon MakeIcon()
    {
        int size = SystemInformation.SmallIconSize.Width;
        using (var bmp = new Bitmap(size, size))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float pad = size / 16f, c = size / 2f;
            using (var face = new SolidBrush(Theme.Soon))
                g.FillEllipse(face, pad, pad, size - 2 * pad - 1, size - 2 * pad - 1);
            using (var hand = new Pen(Theme.Bg, Math.Max(1.5f, size / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(hand, c, c, c, size * 0.24f);
                g.DrawLine(hand, c, c, size * 0.72f, c);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }
}

class Widget : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public event Action Moved;
    public event Action HideRequested;

    readonly ContextMenuStrip menu;
    readonly List<Label> countdowns = new List<Label>();
    readonly List<Label> sessionCountdowns = new List<Label>();  // null where no session limit
    readonly float scale;

    public Widget(ContextMenuStrip menu)
    {
        this.menu = menu;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = Theme.Bg;
        StartPosition = FormStartPosition.Manual;
        Text = "ResetTimer";
        ContextMenuStrip = menu;
        MouseDown += StartDrag;
        using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW
            cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW: keep out of Alt+Tab
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;  // DWMWCP_ROUND (Windows 11 rounded corners)
        try { DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); } catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 hides the widget; the app keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (HideRequested != null) HideRequested();
        }
        base.OnFormClosing(e);
    }

    int S(int px) { return (int)Math.Round(px * scale); }

    public void ShowAt(Point? saved)
    {
        if (saved.HasValue && OnScreen(saved.Value)) Location = saved.Value;
        else
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - S(16), wa.Top + S(16));
        }
        Show();
    }

    bool OnScreen(Point p)
    {
        var bounds = new Rectangle(p, Size);
        foreach (Screen s in Screen.AllScreens)
            if (s.WorkingArea.IntersectsWith(bounds)) return true;
        return false;  // saved spot is off-screen (monitor unplugged)
    }

    public void SetResets(List<Reset> resets, List<string> errors)
    {
        SuspendLayout();
        while (Controls.Count > 0) Controls[0].Dispose();
        countdowns.Clear();
        sessionCountdowns.Clear();

        int pad = S(14), width = S(310), countW = S(165), rowH = S(40), gap = S(8);
        int y = pad;
        foreach (Reset r in resets)
        {
            Add(new Label
            {
                Text = r.Name, AutoSize = false, AutoEllipsis = true,
                Location = new Point(pad, y), Size = new Size(width - pad * 2 - countW - S(8), S(20)),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Theme.Fg,
            });
            Add(new Label
            {
                Text = r.Schedule, AutoSize = true, Location = new Point(pad, y + S(21)),
                Font = new Font("Segoe UI", 8.25f), ForeColor = Theme.Dim,
            });
            countdowns.Add(Add(new Label
            {
                AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(width - pad - countW, y), Size = new Size(countW, rowH),
                Font = new Font("Consolas", 14f), ForeColor = Theme.Fg,
            }));
            y += rowH;

            Label session = null;
            if (r.SessionHours > 0)
            {
                Add(new Label
                {
                    Text = r.SessionLabel, AutoSize = true, Location = new Point(pad, y + S(4)),
                    Font = new Font("Segoe UI", 8.25f), ForeColor = Theme.Dim,
                });
                session = Add(new Label
                {
                    AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
                    Location = new Point(width - pad - countW, y), Size = new Size(countW, S(24)),
                    Font = new Font("Consolas", 11f), ForeColor = Theme.Fg,
                });
                y += S(24);
            }
            sessionCountdowns.Add(session);
            y += gap;
        }
        if (resets.Count > 0) y -= gap;

        var notes = new List<string>();
        foreach (string err in errors) notes.Add("Can't read: " + err);
        if (resets.Count == 0 && errors.Count == 0) notes.Add("No resets yet - right-click > Edit resets...");
        foreach (string note in notes)
        {
            Add(new Label
            {
                Text = note, AutoSize = false, AutoEllipsis = true,
                Location = new Point(pad, y + S(4)), Size = new Size(width - pad * 2, S(18)),
                Font = new Font("Segoe UI", 8.25f), ForeColor = Theme.Warn,
            });
            y += S(22);
        }

        ClientSize = new Size(width, y + pad);
        ResumeLayout();
    }

    public void UpdateTimes(List<Reset> resets, DateTime now)
    {
        for (int i = 0; i < resets.Count && i < countdowns.Count; i++)
        {
            TimeSpan left = resets[i].Next(now) - now;
            countdowns[i].Text = Reset.Long(left);
            countdowns[i].ForeColor = left.TotalHours < 24 ? Theme.Soon : Theme.Fg;

            Label session = sessionCountdowns[i];
            if (session == null) continue;
            DateTime? end = resets[i].SessionEnd;
            session.Text = end.HasValue ? Reset.Hms(end.Value - now) : "not started";
            session.ForeColor = end.HasValue ? Theme.Fg : Theme.Dim;
        }
    }

    T Add<T>(T c) where T : Control
    {
        c.BackColor = Theme.Bg;
        c.ContextMenuStrip = menu;
        c.MouseDown += StartDrag;
        Controls.Add(c);
        return c;
    }

    void StartDrag(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);  // WM_NCLBUTTONDOWN on HTCAPTION; returns when drag ends
        if (Moved != null) Moved();
    }
}

class ResetList : ListBox
{
    public void RefreshAt(int index) { RefreshItem(index); }
}

class Editor : Form
{
    readonly List<Reset> items = new List<Reset>();
    readonly ResetList list = new ResetList();
    readonly TextBox name = new TextBox();
    readonly ComboBox day = new ComboBox();
    readonly DateTimePicker time = new DateTimePicker();
    readonly CheckBox hasSession = new CheckBox();
    readonly NumericUpDown hours = new NumericUpDown();
    readonly Label preview = new Label();
    readonly Button remove;
    readonly float scale;
    bool loading;

    public bool Saved { get; private set; }
    public List<Reset> Result { get { return items; } }

    public Editor(List<Reset> current, Icon icon)
    {
        foreach (Reset r in current) items.Add(r.Copy());
        Text = "Edit resets";
        Icon = icon;
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
        ClientSize = new Size(S(470), S(322));

        list.IntegralHeight = false;
        list.SetBounds(S(12), S(12), S(190), S(256));
        list.SelectedIndexChanged += (s, e) => ShowSelected();
        Controls.Add(list);
        AddButton("Add", S(12), (s, e) => AddReset());
        remove = AddButton("Remove", S(110), (s, e) => RemoveSelected());

        int x = S(218), w = S(240);
        AddLabel("Name", x, S(12));
        name.SetBounds(x, S(32), w, S(24));
        name.TextChanged += (s, e) => EditSelected();
        AddLabel("Resets every", x, S(66));
        day.DropDownStyle = ComboBoxStyle.DropDownList;
        day.SetBounds(x, S(86), w, S(24));
        for (int i = 0; i < 7; i++) day.Items.Add(((DayOfWeek)((i + 1) % 7)).ToString());  // Monday first
        day.SelectedIndexChanged += (s, e) => EditSelected();
        AddLabel("At (your local time)", x, S(120));
        time.Format = DateTimePickerFormat.Custom;
        time.ShowUpDown = true;
        time.SetBounds(x, S(140), S(110), S(24));
        time.ValueChanged += (s, e) => EditSelected();
        preview.SetBounds(x, S(220), w, S(48));
        preview.ForeColor = SystemColors.GrayText;
        hasSession.Text = "Also has a limit every";
        hasSession.AutoSize = true;
        hasSession.Location = new Point(x, S(180));
        hasSession.CheckedChanged += (s, e) => { hours.Enabled = hasSession.Checked && hasSession.Enabled; EditSelected(); };
        Controls.AddRange(new Control[] { name, day, time, preview, hasSession });
        hours.Minimum = 1;
        hours.Maximum = 24;
        hours.Value = 5;
        hours.SetBounds(hasSession.Right + S(4), S(178), S(48), S(24));
        hours.ValueChanged += (s, e) => EditSelected();
        Controls.Add(hours);
        AddLabel("hours", hours.Right + S(6), S(181));

        Button save = AddButton("Save", S(270), (s, e) => { Saved = true; Close(); });
        Button cancel = AddButton("Cancel", S(366), (s, e) => Close());
        AcceptButton = save;
        CancelButton = cancel;

        foreach (Reset r in items) list.Items.Add(r);
        if (items.Count > 0) list.SelectedIndex = 0;
        else ShowSelected();
        ApplyClockFormat();
    }

    public void ApplyClockFormat()
    {
        time.CustomFormat = Reset.Use12h ? "h:mm tt" : "HH:mm";
        for (int i = 0; i < items.Count; i++) list.RefreshAt(i);
        if (list.SelectedIndex >= 0) UpdatePreview(items[list.SelectedIndex]);
    }

    int S(int px) { return (int)Math.Round(px * scale); }

    Button AddButton(string text, int x, EventHandler click)
    {
        var b = new Button { Text = text };
        b.SetBounds(x, S(280), S(92), S(30));
        b.Click += click;
        Controls.Add(b);
        return b;
    }

    void AddLabel(string text, int x, int y)
    {
        Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
    }

    void ShowSelected()
    {
        if (loading) return;
        int i = list.SelectedIndex;
        name.Enabled = day.Enabled = time.Enabled = hasSession.Enabled = remove.Enabled = i >= 0;
        loading = true;
        if (i >= 0)
        {
            Reset r = items[i];
            name.Text = r.Name;
            day.SelectedIndex = ((int)r.Day + 6) % 7;
            time.Value = DateTime.Today.Add(r.Time);
            hasSession.Checked = r.SessionHours > 0;
            hours.Value = r.SessionHours > 0 ? r.SessionHours : 5;
            UpdatePreview(r);
        }
        else
        {
            name.Text = "";
            hasSession.Checked = false;
            preview.Text = "Click Add to track a weekly reset.";
        }
        hours.Enabled = hasSession.Enabled && hasSession.Checked;
        loading = false;
    }

    void EditSelected()
    {
        int i = list.SelectedIndex;
        if (loading || i < 0) return;
        Reset r = items[i];
        r.Name = name.Text;
        r.Day = (DayOfWeek)((day.SelectedIndex + 1) % 7);
        r.Time = new TimeSpan(time.Value.Hour, time.Value.Minute, 0);
        r.SessionHours = hasSession.Checked ? (int)hours.Value : 0;
        if (r.SessionHours == 0) r.SessionEnd = null;
        list.RefreshAt(i);
        UpdatePreview(r);
    }

    void UpdatePreview(Reset r)
    {
        DateTime now = DateTime.Now, next = r.Next(now);
        preview.Text = "Next reset: " + Reset.When(next) + "\r\n(in " + Reset.Short(next - now) + ")";
    }

    void AddReset()
    {
        var r = new Reset { Name = "New reset", Day = DayOfWeek.Sunday, Time = TimeSpan.Zero };
        items.Add(r);
        list.Items.Add(r);
        list.SelectedIndex = items.Count - 1;
        name.Focus();
        name.SelectAll();
    }

    void RemoveSelected()
    {
        int i = list.SelectedIndex;
        if (i < 0) return;
        items.RemoveAt(i);
        list.Items.RemoveAt(i);
        if (items.Count > 0) list.SelectedIndex = Math.Min(i, items.Count - 1);
        else ShowSelected();
    }
}

// Opened from a session row in the tray menu: start the window now, or type in the reset time
// the AI shows you, or clear it.
class SessionDialog : Form
{
    readonly DateTimePicker time = new DateTimePicker();

    public DateTime? End { get; private set; }

    public SessionDialog(Reset r, Icon icon)
    {
        Text = r.Name + " " + r.SessionLabel;
        Icon = icon;
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        float scale;
        using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
        Func<int, int> S = px => (int)Math.Round(px * scale);
        ClientSize = new Size(S(330), S(168));
        End = r.SessionEnd;

        DateTime fresh = DateTime.Now.AddHours(r.SessionHours);
        var start = new Button { Text = "Start now (resets " + Reset.Clock(fresh.TimeOfDay) + ")" };
        start.SetBounds(S(12), S(12), S(306), S(32));
        start.Click += (s, e) => Finish(DateTime.Now.AddHours(r.SessionHours));

        Controls.Add(new Label { Text = "Or set when it resets (your local time):", AutoSize = true, Location = new Point(S(12), S(58)) });
        time.Format = DateTimePickerFormat.Custom;
        time.CustomFormat = Reset.Use12h ? "h:mm tt" : "HH:mm";
        time.ShowUpDown = true;
        time.Value = new DateTime(2000, 1, 1).Add((r.SessionEnd ?? fresh).TimeOfDay);
        time.SetBounds(S(12), S(80), S(110), S(24));
        var set = new Button { Text = "Set" };
        set.SetBounds(S(128), S(78), S(70), S(28));
        set.Click += (s, e) =>
        {
            DateTime now = DateTime.Now, end = now.Date.Add(time.Value.TimeOfDay);
            Finish(end <= now ? end.AddDays(1) : end);
        };

        var clear = new Button { Text = "Clear", Enabled = r.SessionEnd.HasValue };
        clear.SetBounds(S(12), S(126), S(92), S(30));
        clear.Click += (s, e) => Finish(null);
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        cancel.SetBounds(S(226), S(126), S(92), S(30));

        Controls.AddRange(new Control[] { start, time, set, clear, cancel });
        // Running: Enter applies the edited time. Not started: Enter starts it.
        AcceptButton = r.SessionEnd.HasValue ? set : start;
        ActiveControl = r.SessionEnd.HasValue ? (Control)time : start;
        CancelButton = cancel;
    }

    void Finish(DateTime? end)
    {
        End = end;
        DialogResult = DialogResult.OK;
    }
}
