# Reset Timer

**See exactly when your AI usage limits reset.**

Reset Timer is a tiny Windows app that counts down to the weekly limit resets of AI tools like Claude, and
optionally to rolling limits like Claude's 5-hour session window. It has three modes:

- **Tray only:** click the tray icon and every countdown is right there in the menu.
- **In the taskbar:** the next reset sits in the empty space of your taskbar, next to the clock, so it never
  covers anything. Hover over it and a card springs up with every countdown, progress bars and a **Start**
  button for session windows.
- **Floating island:** the same, as a small black pill you can put anywhere on screen. It fades away and lets
  clicks through when your mouse passes over it, so it doesn't block what's behind it.

And in all of them:

- **Tiny:** a single exe of about 45 KB, with nothing to install.
- **Light on your PC:** about 30 to 40 MB of RAM, and practically no CPU while idle. The island only animates
  while it opens or closes, and a collapsed island redraws once a minute.
- **Notifies you:** a Windows notification when a weekly limit or a session window resets.
- **Any AI:** track as many tools as you like, each with its own reset day and time.

## Download

1. Download `ResetTimer.exe` from the [latest release](https://github.com/dapss/ResetTimer/releases/latest).
2. Put it in any folder, such as `Documents\ResetTimer`, and run it.

Requires 64-bit Windows 10 or 11.

> **"Windows protected your PC"?** Reset Timer isn't code-signed, so Windows SmartScreen may warn you the first
> time. Click **More info**, then **Run anyway**. If you'd rather not trust a downloaded exe, you can
> [build it yourself](#build-from-source) from the source in this repo.

## Usage

Reset Timer appears as a green clock in the system tray. If you don't see it, click the `^` arrow on the taskbar
and drag the icon onto the taskbar to keep it visible.

| To | Do this |
|---|---|
| See every countdown | Click the icon, or hover over the island |
| Click something behind the floating island | Just click it: the island fades away and lets the click through |
| See a short summary | Hover over the icon |
| Switch between tray only, taskbar and floating | Click the icon and choose **Mode** |
| Move the island in the taskbar | Drag it left or right |
| Move the floating island | Open it, then drag the card |
| Add, change or remove an AI | Right-click the icon and choose **Edit resets...** |
| Start a session window | Right-click the icon and click the AI's **5-hour limit** row |
| Show times as AM/PM | Right-click the icon and choose **12-hour time (AM/PM)** |
| Turn notifications on or off | Right-click the icon and choose **Notify when limits reset** |
| Start Reset Timer when you sign in | Right-click the icon and choose **Start with Windows** |
| Quit | Right-click the icon and choose **Exit** |

### Weekly resets

In **Edit resets...**, give each AI a name, the day its weekly limit resets and the time, in your own time zone.
Each AI needs its own name. If you close the editor with unsaved changes, it asks whether to save them;
**Cancel** throws them away.

A countdown turns green when its reset is less than 24 hours away, when it also starts showing seconds, and
starts over for the next week once it passes.

### Session limits

Some limits are rolling windows that start when you start using the AI, such as Claude's 5-hour limit. To track
one, tick **Also has a limit every 5 hours** for that AI in **Edit resets...** (you can change the number of
hours). Then, when you start a session, either click **Start** on the open island, or click the session's row in
the tray menu and either:

- click **Start now**, or
- type in the reset time the AI shows you and click **Set**.

Started one by mistake? Right after you click **Start** on the island, an **Undo** button appears for a few
seconds. The island also shows what time a running window resets.

To change a running window's reset time later, click its countdown on the island or its row in the tray menu.
When the window ends you get a notification, and the row goes back to "not started" until your next session.

### The island

The collapsed island shows the soonest running session window in orange, or else the soonest weekly reset. Its
ring fills up as that reset gets closer.

**In the taskbar** it starts in the empty space just left of the clock and tray icons. Hover over it for a moment,
or click it, and its card opens above the taskbar. Move the mouse away to close it. Drag it left or right to
put it anywhere along the taskbar; it remembers the spot. It follows your light or dark taskbar, and if your
taskbar hides itself, the island hides with it. A taskbar on the left or right side of the screen has no room
for it, so there it floats instead.

**Floating,** it stays out of your way:

- **When your mouse moves onto it, it fades away** and lets clicks through to whatever is behind it, such as a
  browser tab or a title bar.
- **Rest the mouse on it to open it.** It fades back in as you wait, then opens after about half a second. If you
  click through it, it stays faded until you move away. Move the mouse off the card to close it.
- **It hides itself while a full-screen app is in front,** such as a game, a full-screen video or a presentation.

To move it, open it and drag the card anywhere; it remembers the spot. In the bottom half of the screen it opens
upward. To hide it completely, switch to **Tray only**.

If you move the exe to another folder, **Start with Windows** follows it the next time you run it.

## Limitations

- **You enter the reset times.** Reset Timer doesn't connect to any AI service, so it can't read your actual
  limits or usage. It only counts down to the times you give it.
- **Session windows are started by hand.** It can't tell when you start using an AI.
- **Missed notifications aren't repeated.** If a session window ends while your PC is off, it's cleared quietly
  once Reset Timer starts again.
- **Full-screen detection relies on Windows.** The island hides when Windows reports a full-screen app. A few
  borderless-windowed games don't report that, so switch to **Tray only** if it shows up over one.
- **The floating island needs a mouse.** It opens when the mouse rests on it, so on a touch screen use the
  taskbar mode or the tray icon instead.
- **The taskbar mode can't see your taskbar icons.** If you pin enough apps to reach it, drag it to an empty
  spot.
- **The tray menu and editor are always light,** even in dark mode, while the island is always dark.

## Build from source

You don't need Visual Studio or any SDK. Reset Timer compiles with the C# compiler that comes with Windows
(.NET Framework 4).

```
git clone https://github.com/dapss/ResetTimer.git
cd ResetTimer
build.bat
```

This produces `ResetTimer.exe` in the same folder. The island is in [`Island.cs`](Island.cs), and everything else
is in [`ResetTimer.cs`](ResetTimer.cs).

## Uninstall

1. If you turned on **Start with Windows**, right-click the tray icon and turn it off.
2. Choose **Exit**.
3. Delete the folder containing `ResetTimer.exe`.

Reset Timer only writes these things:

- `resets.txt`, `sessions.txt` and `settings.txt`, next to the exe. If that folder is read-only, such as
  `Program Files`, they go in `%APPDATA%\ResetTimer` instead, so delete that folder too if it exists.
- The **Start with Windows** entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, which step 1
  removes.

## License

[MIT](LICENSE)
