using System.Windows;

namespace Cooldown;

public partial class App : Application
{
    Mutex? single, legacy;
    EventWaitHandle? showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        single = new Mutex(true, "Cooldown.SingleInstance", out bool first);
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Cooldown.Show");
        if (!first)
        {
            // Already running: ask that copy to open its window, then quit. Windows only lets the app
            // you just launched come to the front, so hand that permission to the running copy first.
            Native.AllowSetForegroundWindow(-1);  // ASFW_ANY
            showSignal.Set();
            Shutdown();
            return;
        }

        // Cooldown used to be called Reset Timer, which used this lock. Holding it too means the two
        // never run side by side, counting down twice in the taskbar.
        legacy = new Mutex(true, "ResetTimer.SingleInstance", out bool noLegacy);
        if (!noLegacy)
        {
            System.Windows.MessageBox.Show("Reset Timer, the older version of Cooldown, is still running.\n\n" +
                "Quit it from its tray icon (right-click > Exit), then start Cooldown again.",
                "Cooldown", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var app = new AppController();
        app.Start();

        // Running Cooldown again opens the window of the copy that's already running.
        var listener = new Thread(() =>
        {
            while (showSignal.WaitOne())
                Dispatcher.BeginInvoke(() => app.OpenMain());
        }) { IsBackground = true };
        listener.Start();
    }
}
