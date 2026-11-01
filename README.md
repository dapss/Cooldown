# Reset Timer

A tiny Windows 11 tray app that counts down to weekly AI usage resets (e.g. Claude's weekly limit).

- **Right-click the tray icon** to see live countdowns for every reset, no window needed.
- **Hover** the icon for a short summary; **left-click** to show/hide a small always-on-top widget.
- **Edit resets...** opens a window to add, rename, or remove resets and set their day and time.
- Optional 12-hour (AM/PM) display, start with Windows, and widget position memory.

## Build

No install needed: it compiles with the C# compiler that ships with Windows (.NET Framework 4).

```
build.bat
```

This produces `ResetTimer.exe` (~26 KB). Run it and look for the green clock in the system tray.
New tray icons start hidden behind the `^` arrow; drag it onto the taskbar to keep it visible.

## Files

- `ResetTimer.cs`: the whole app.
- `resets.txt`: your resets, created next to the exe on first run (`Name | Day | HH:mm`).
- `settings.txt`: widget and display preferences.
