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

        // Draw on the CPU, not the GPU. Cooldown's windows are small and mostly still, and using the GPU
        // loads the graphics driver into the app: about 75 MB of memory, against little CPU for drawing.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Cooldown runs for weeks at a time: one unexpected error is noted in cooldown.log, not a
        // reason to vanish from the taskbar.
        DispatcherUnhandledException += (_, error) =>
        {
            Core.Store.Log("Unexpected error", error.Exception);
            error.Handled = true;
        };

        var app = new AppController();
        app.Start();
#if DEBUG
        if (e.Args.Length == 2 && e.Args[0] == "--simulate-toast") app.SimulateToast(e.Args[1]);
#endif

        // Running Cooldown again opens the window of the copy that's already running.
        var listener = new Thread(() =>
        {
            while (showSignal.WaitOne())
                Dispatcher.BeginInvoke(() => app.OpenMain());
        }) { IsBackground = true };
        listener.Start();
    }

    static bool fluentLoaded;

    // WPF-UI's theme and control styles, for the window and the menu. The island doesn't use them, so
    // they load the first time the window or the menu opens, not at startup.
    public static void EnsureFluentResources()
    {
        if (fluentLoaded) return;
        fluentLoaded = true;
        var merged = Current.Resources.MergedDictionaries;
        merged.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = Wpf.Ui.Appearance.ApplicationTheme.Dark });
        merged.Add(new Wpf.Ui.Markup.ControlsDictionary());
    }
}
