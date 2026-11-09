using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace Cooldown.Core;

// resets.txt holds the reset list, sessions.txt the running session windows, settings.txt the
// preferences. Same plain-text files as version 1, so existing setups carry over.
public static class Store
{
    public static readonly string Dir = FindDataFolder();
    public static readonly string ResetsPath = Path.Combine(Dir, "resets.txt");
    static readonly string SessionsPath = Path.Combine(Dir, "sessions.txt");
    static readonly string[] TimeFormats = { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "h tt", "htt" };
    const string StampFormat = "yyyy-MM-dd HH:mm:ss";
    const string Header =
        "# Cooldown - edit from the app: open it from the tray icon\r\n" +
        "# Format: Name | Day | Time (your local time) | optional session limit, e.g. 5h\r\n";

    // True when there was no resets.txt yet: the first time the app runs.
    public static bool FirstRun { get; private set; }

    public static List<Reset> LoadResets(List<string> errors)
    {
        if (!File.Exists(ResetsPath) || new FileInfo(ResetsPath).Length == 0)
        {
            FirstRun = true;
            File.WriteAllText(ResetsPath, Header + "\r\nClaude | Sunday | 16:00 | 5h\r\n");
        }
        var list = new List<Reset>();
        foreach (string raw in File.ReadAllLines(ResetsPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            string[] parts = line.Split('|');
            int hours = 0;
            if (parts.Length < 3 || parts.Length > 4 || parts[0].Trim().Length == 0
                || !TryParseDay(parts[1].Trim(), out DayOfWeek day)
                || !DateTime.TryParseExact(parts[2].Trim().ToUpperInvariant(), TimeFormats,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
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

    public static void SaveResets(IEnumerable<Reset> resets)
    {
        var sb = new StringBuilder(Header).Append("\r\n");
        foreach (Reset r in resets)
        {
            sb.Append($"{CleanName(r.Name)} | {r.Day} | {DateTime.Today.Add(r.Time):HH:mm}");
            if (r.SessionHours > 0) sb.Append($" | {r.SessionHours}h");
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
                if (p.Length == 2 && DateTime.TryParseExact(p[1].Trim(), StampFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime end))
                    ends[p[0].Trim()] = end;
            }
        }
        catch (IOException) { }  // no sessions saved yet
        foreach (Reset r in resets)
            if (r.SessionHours > 0 && ends.TryGetValue(r.Name, out DateTime end)) r.SessionEnd = end;
    }

    public static void SaveSessions(IEnumerable<Reset> resets)
    {
        var sb = new StringBuilder();
        foreach (Reset r in resets)
            if (r.SessionHours > 0 && r.SessionEnd.HasValue)
                sb.Append($"{CleanName(r.Name)} | {r.SessionEnd.Value.ToString(StampFormat, CultureInfo.InvariantCulture)}\r\n");
        try { File.WriteAllText(SessionsPath, sb.ToString()); } catch (Exception) { }
    }

    // Next to the exe when that folder is writable, so the app stays portable. Otherwise, such as in
    // Program Files, in the user's AppData folder.
    static string FindDataFolder()
    {
        string local = AppContext.BaseDirectory;
        if (CanWrite(Path.Combine(local, "resets.txt"))) return local;
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string folder = Path.Combine(appData, "Cooldown"), old = Path.Combine(appData, "ResetTimer");
        try
        {
            // From when Cooldown was called Reset Timer: bring its resets and settings along.
            if (!Directory.Exists(folder) && Directory.Exists(old)) Directory.Move(old, folder);
            Directory.CreateDirectory(folder);
        }
        catch (Exception) { }
        return folder;
    }

    static bool CanWrite(string path)
    {
        try
        {
            using (new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite)) { }
            return true;
        }
        catch (Exception) { return false; }
    }

    public static string CleanName(string name)
    {
        name = name.Replace('|', '/').Trim();
        return name.Length == 0 ? "Untitled" : name;
    }

    static bool TryParseHours(string text, out int hours) =>
        int.TryParse(text.Trim().TrimEnd('h', 'H'), out hours) && hours >= 1 && hours <= 24;

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

public enum DisplayMode { Tray, Island, Taskbar }

public class Settings
{
    static readonly string FilePath = Path.Combine(Store.Dir, "settings.txt");
    public DisplayMode Mode = DisplayMode.Taskbar;
    public bool Notify = true;
    public bool Use12h = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');  // follow Windows until set
    public System.Windows.Point? IslandPosition;  // floating: the pill's top-center, in screen pixels
    public int TaskbarSpot = -1;                  // the pill's center in the taskbar, in pixels from its right end

    public static Settings Load()
    {
        var s = new Settings();
        bool sawMode = false;
        try
        {
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string[] kv = line.Split('=');
                if (kv.Length != 2) continue;
                string v = kv[1].Trim();
                switch (kv[0].Trim())
                {
                    case "mode":
                        s.Mode = v == "island" ? DisplayMode.Island : v == "tray" ? DisplayMode.Tray : DisplayMode.Taskbar;
                        sawMode = true;
                        break;
                    case "widget": if (!sawMode) s.Mode = v == "1" ? DisplayMode.Island : DisplayMode.Tray; break;
                    case "ampm": s.Use12h = v == "1"; break;
                    case "notify": s.Notify = v == "1"; break;
                    case "island":
                        string[] xy = v.Split(',');
                        s.IslandPosition = new System.Windows.Point(int.Parse(xy[0]), int.Parse(xy[1]));
                        break;
                    case "taskbar": s.TaskbarSpot = int.Parse(v); break;
                }
            }
        }
        catch (Exception) { }  // missing or damaged file: defaults
        return s;
    }

    public void Save()
    {
        string mode = Mode switch { DisplayMode.Island => "island", DisplayMode.Tray => "tray", _ => "taskbar" };
        var text = new StringBuilder($"mode={mode}\r\nampm={(Use12h ? 1 : 0)}\r\nnotify={(Notify ? 1 : 0)}\r\n");
        if (IslandPosition is { } p) text.Append($"island={(int)p.X},{(int)p.Y}\r\n");
        if (TaskbarSpot >= 0) text.Append($"taskbar={TaskbarSpot}\r\n");
        try { File.WriteAllText(FilePath, text.ToString()); } catch (Exception) { }
    }
}

// Start with Windows: only the Run entry is written. Windows adds its own Startup apps approval for
// it at the next sign-in; writing that approval ourselves makes Windows skip the entry.
public static class Startup
{
    const string AppName = "Cooldown";
    const string OldName = "ResetTimer";  // Cooldown's name before
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string Command => "\"" + Environment.ProcessPath + "\"";

    public static bool Enabled
    {
        get
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey);
            return run?.GetValue(AppName) != null || run?.GetValue(OldName) != null;
        }
        set
        {
            using RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) run.SetValue(AppName, Command);
            else run.DeleteValue(AppName, false);
            run.DeleteValue(OldName, false);
        }
    }

    // Keeps Start with Windows pointing at this exe: after the exe is moved, and after the rename from
    // Reset Timer, whose entry is replaced by Cooldown's. Debug builds leave it alone, so running the
    // app from Visual Studio or `dotnet run` never takes over the real one.
    public static void RefreshPath()
    {
#if !DEBUG
        try
        {
            using RegistryKey? run = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (run == null) return;
            bool on = run.GetValue(AppName) != null || run.GetValue(OldName) != null;
            if (!on) return;
            if (!string.Equals(run.GetValue(AppName) as string, Command, StringComparison.OrdinalIgnoreCase))
                run.SetValue(AppName, Command);
            run.DeleteValue(OldName, false);
        }
        catch (Exception) { }  // registry locked down: leave it as is
#endif
    }
}
