# Changelog

What changed in each version of Cooldown. When a version is released, its section here becomes the notes on its
[GitHub release](https://github.com/dapss/Cooldown/releases).

## Unreleased

### Maintenance
- Every change is now built and tested automatically, and a version tag builds `Cooldown.exe` and publishes the release.

## 2.1.1 (2026-11-11)

A small release of fixes.

### Fixed
- **No more false "weekly limit has reset" notification** when you change an AI's reset day or time to a later one.
- **Your list of AIs is safer.** Cooldown now saves its files in a way that a crash or power cut can't leave them empty, and it no longer replaces an empty `resets.txt` with the example.
- **An AI whose name starts with `#`** no longer disappears the next time Cooldown starts. The `#` is dropped from the start of the name.
- **Cooldown no longer closes** when `resets.txt` is locked by an editor or a sync tool while you make a change, or when something unexpected goes wrong. If that ever happens it's noted in `cooldown.log`, next to your other Cooldown files.

### Updating
Quit Cooldown (right-click the tray icon > **Exit**), replace `Cooldown.exe` with the one below, and run it. Your resets and settings stay as they are.

Needs the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0), like 2.1.0.

## 2.1.0 (2026-11-10)

### Notifications that do something
- **Real Windows notifications** instead of the old balloon tips. When a session window ends, click **Start new session** right on the notification to start the next one.
- **Optional reminders** before a weekly reset (1 hour or 1 day before) and before a session window ends (15 minutes to 1 hour before), so you can use what's left first. Turn them on in **Settings > Reminders**.
- **Send a test** in Settings shows you what a notification looks like.

### Lighter
- Uses about **12 MB** in Task Manager while it sits in your taskbar, down from about 55 MB.

### Easier to use
- Cooldown's **mint** is now the accent color throughout, instead of your Windows accent color.
- The countdown in your taskbar opens **on click**, so it no longer pops open when your mouse passes over it on the way to the tray. Prefer hover? Turn on **Open on hover in the taskbar** in Settings.
- The window **reopens where you left it**, at the same size.
- Respects Windows' **Animation effects** setting.
- Every control has a **screen reader** label.
- A roomier **"set when it resets"** row for session windows.

## 2.0.0 (2026-11-09)

The first release as Cooldown, rebuilt from the ground up (it used to be called Reset Timer).

- **A countdown in your taskbar**, next to the clock, with a ring that fills up as your reset gets closer. Or make it a floating pill, or keep it in the tray.
- **A Windows 11 style app** to add your AIs, set their reset day and time, and start session windows, with **Undo** if you start one by mistake. Follows your light or dark mode.
- **Notifications** when a weekly limit or a session window resets.
- Hides itself behind full-screen games, videos and presentations.
