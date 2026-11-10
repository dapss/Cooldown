using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Cooldown;

// Windows notifications (the kind in Notification Center), with buttons that act on an AI: "Start new
// session" starts its 5-hour window, "Open" shows it in Cooldown. Clicking the notification itself
// opens Cooldown too. If Windows notifications fail, it falls back to the tray icon's balloon.
//
// Works for an app that isn't installed through the Store: the library registers Cooldown with
// Windows the first time it notifies, and Windows starts Cooldown if a button is clicked while it's
// closed.
public sealed class Notifier
{
    readonly AppController app;
    readonly Action<string, string> fallback;

    public Notifier(AppController app, Action<string, string> fallback)
    {
        this.app = app;
        this.fallback = fallback;
        // Clicks arrive on a background thread.
        ToastNotificationManagerCompat.OnActivated += e =>
            Application.Current?.Dispatcher.BeginInvoke(() => Handle(ToastArguments.Parse(e.Argument)));
    }

    public void Show(string title, string text, ResetView? about = null, bool offerStart = false)
    {
        try
        {
            var toast = new ToastContentBuilder().AddText(title).AddText(text).AddArgument("action", "open");
            if (about != null) toast.AddArgument("ai", about.Name);
            if (about != null && offerStart)
                toast.AddButton(new ToastButton().SetContent("Start new session")
                    .AddArgument("action", "start").AddArgument("ai", about.Name));
            toast.AddButton(new ToastButton().SetContent("Open Cooldown")
                .AddArgument("action", "open").AddArgument("ai", about?.Name ?? ""));
            toast.Show();
        }
        catch (Exception)
        {
            fallback(title, text);
        }
    }

#if DEBUG
    // Debug builds only: run what a click on a notification button would, from the command line,
    // e.g. --simulate-toast "action=start;ai=Claude".
    public void Simulate(string arguments) => Handle(ToastArguments.Parse(arguments));
#endif

    void Handle(ToastArguments args)
    {
        args.TryGetValue("ai", out string? name);
        ResetView? view = app.Resets.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
        args.TryGetValue("action", out string? action);
        if (action == "start" && view != null && view.HasSession)
        {
            // Only if it hasn't been started since: never replace a running window.
            if (!view.SessionRunning) app.StartSession(view);
            app.OpenMain(view);
        }
        else app.OpenMain(view);
    }
}
