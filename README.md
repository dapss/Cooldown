# Cooldown

**Know exactly when your AI limits come back.**

You hit your Claude limit. When does it reset? Cooldown counts down to it, right in your Windows taskbar: the
weekly limit of every AI tool you use, and rolling limits like Claude's 5-hour session window.

- **In your taskbar.** The next reset sits in the empty space next to the clock, with a ring that fills up as
  it gets closer. Hover over it and a card springs up with every countdown.
- **Or floating, or tray only.** Put a small pill anywhere on screen instead (it fades away when your mouse
  passes over it, so it never blocks anything), or keep it all in the tray icon.
- **Session windows too.** Click **Start** when you start chatting, or type in the reset time your AI shows
  you. Started one by mistake? **Undo** is right there.
- **Notifies you** when a weekly limit or a session window resets, with a **Start new session** button right on
  the notification. Optional reminders warn you before a reset, so you can use what's left first.
- **Any AI,** each with its own reset day and time.
- **Light on your PC:** about 10 to 15 MB in Task Manager while it sits in your taskbar.
- **Fits Windows 11:** follows your light or dark mode, with the translucent Mica background.

Cooldown used to be called **Reset Timer**. It keeps your resets and settings when you switch.

## Download

1. Download `Cooldown.exe` from the [latest release](https://github.com/dapss/Cooldown/releases/latest).
2. Put it in any folder, such as `Documents\Cooldown`, and run it.

Requires 64-bit Windows 10 or 11 and the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
(Windows offers to install it the first time if you don't have it).

> **"Windows protected your PC"?** Cooldown isn't code-signed, so Windows SmartScreen may warn you the first
> time. Click **More info**, then **Run anyway**. If you'd rather not trust a downloaded exe, you can
> [build it yourself](#build-from-source).

## Usage

The first time you run Cooldown, it opens so you can set up your first AI. After that it lives in your taskbar
and tray. Click the tray icon, or run `Cooldown.exe` again, to open it.

| To | Do this |
|---|---|
| See every countdown | Hover over the countdown in your taskbar, or click the tray icon |
| Add, change or remove an AI | Open Cooldown and pick the AI, or **Add an AI** |
| Start a session window | Click **Start** on the card, or **Start now** in Cooldown |
| Move the countdown along the taskbar | Drag it |
| Choose taskbar, floating or tray only | **Settings** > **Show the countdown**, or right-click > **Show countdown** |
| 12-hour time, notifications, reminders, Start with Windows | **Settings** |
| Quit | Right-click the tray icon > **Exit** |

Every change is saved as you make it.

### Weekly resets

Give each AI a name, the day its weekly limit resets and the time, in your own time zone. Its countdown turns
green when the reset is less than a day away, and starts over for the next week once it passes.

### Session limits

Some limits are rolling windows that start when you start using the AI, such as Claude's 5-hour limit. Turn on
**Also has a rolling limit** for that AI and set the hours. Then, when you start a session, either click
**Start**, or type in the reset time your AI shows you and click **Set**. When the window ends you get a
notification, and it goes back to "not started" until your next session. Click **Start new session** on that
notification to start the next window straight away.

### Notifications and reminders

Cooldown uses Windows notifications, so they also show up in Notification Center. Click **Send a test** in
**Settings** to see one. Under **Reminders** you can also be warned **before a weekly reset** (1 hour or 1 day
before) and **before a session window ends** (15 minutes to 1 hour before): usage you haven't used by then
doesn't carry over. With Windows' **Do not disturb** on, notifications go straight to Notification Center
without popping up.

### The countdown in your taskbar

It starts in the empty space just left of the clock. Drag it anywhere along the taskbar; it remembers the spot.
It shows your soonest running session in orange, or else your soonest weekly reset. It follows a light or dark
taskbar, hides with an auto-hiding taskbar, and hides behind full-screen games, videos and presentations.

The floating pill works the same way, with one extra trick: when your mouse moves onto it, it fades away and lets
clicks through to whatever is behind it. Rest the mouse on it for a moment to open it.

## Limitations

- **You enter the reset times.** Cooldown doesn't connect to any AI service, so it can't read your actual
  limits or usage. It only counts down to the times you give it.
- **Session windows are started by hand.** It can't tell when you start using an AI.
- **Missed notifications aren't repeated.** If a session window ends while your PC is off, it's cleared quietly.
- **It can't see your taskbar icons.** If you pin enough apps to reach the countdown, drag it to an empty spot.
- **Full-screen detection relies on Windows.** A few borderless-windowed games don't report being full
  screen; switch to tray only while you play those.

## Build from source

You need the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```
git clone https://github.com/dapss/Cooldown.git
cd Cooldown
build.bat
```

This produces `Cooldown.exe` next to `build.bat`. The app is a WPF app using [WPF-UI](https://github.com/lepoco/wpfui),
in [`src/Cooldown`](src/Cooldown):

- [`AppController.cs`](src/Cooldown/AppController.cs): the clock, notifications and saving
- [`MainWindow.xaml`](src/Cooldown/MainWindow.xaml): the app window
- [`IslandWindow.xaml`](src/Cooldown/IslandWindow.xaml): the countdown in the taskbar, or floating
- [`TrayIcon.cs`](src/Cooldown/TrayIcon.cs): the tray icon and its menu
- [`Core/`](src/Cooldown/Core): the reset model and the data files

To run it while working on it, `dotnet run --project src/Cooldown`. Debug builds keep their own data next to
their exe and never change your **Start with Windows** entry.

## Uninstall

1. Open Cooldown's **Settings** and turn off **Start with Windows**.
2. Right-click the tray icon and choose **Exit**.
3. Delete the folder containing `Cooldown.exe`.

Cooldown only writes `resets.txt`, `sessions.txt` and `settings.txt` next to the exe (or, if that folder is
read-only, in `%APPDATA%\Cooldown`), and the **Start with Windows** entry, which step 1 removes. The first time
it notifies you, Windows also records Cooldown as an app that sends notifications (its name and icon, so the
buttons on a notification can start it). That's harmless to leave, and Windows lists it under
**Settings > System > Notifications**.

## License

[MIT](LICENSE)
