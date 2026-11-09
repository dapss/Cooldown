using System.ComponentModel;
using System.Windows.Input;
using Cooldown.Core;

namespace Cooldown;

// A Reset plus everything the UI shows about it, refreshed once a second. The main window, the tray
// menu and the island all bind to the same views, so they always agree.
public class ResetView : INotifyPropertyChanged
{
    readonly AppController app;
    DateTime now = DateTime.Now;

    public Reset Model { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public ResetView(Reset model, AppController app)
    {
        Model = model;
        this.app = app;
        StartCommand = new RelayCommand(() => app.StartSession(this));
        UndoCommand = new RelayCommand(() => app.UndoStart(this));
        OpenCommand = new RelayCommand(() => app.OpenMain(this));
    }

    public ICommand StartCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand OpenCommand { get; }

    public string Name => Model.Name;

    TimeSpan Left => Model.Next(now) - now;
    public string Countdown => Format.Long(Left);
    public double WeekProgress => Math.Clamp(1 - Left.TotalDays / 7, 0, 1);
    public bool Soon => Left.TotalHours < 24;
    public string ScheduleText => "Resets " + Model.Schedule;
    public string NextText => Format.When(Model.Next(now));

    public bool HasSession => Model.SessionHours > 0;
    public bool SessionRunning => HasSession && Model.SessionEnd.HasValue;
    public bool SessionIdle => HasSession && !Model.SessionEnd.HasValue;
    public string SessionLabel => Model.SessionLabel;
    TimeSpan SessionLeft => Model.SessionEnd.HasValue ? Model.SessionEnd.Value - now : TimeSpan.Zero;
    public string SessionCountdown => Format.Hms(SessionLeft);
    public double SessionProgress =>
        Model.SessionHours > 0 ? Math.Clamp(1 - SessionLeft.TotalHours / Model.SessionHours, 0, 1) : 0;
    public string SessionEndClock => Model.SessionEnd.HasValue ? Format.Clock(Model.SessionEnd.Value.TimeOfDay) : "";
    public string SessionResetText => (ShowUndo ? "Started. Resets at " : "Resets at ") + SessionEndClock;
    public bool ShowUndo => app.CanUndo(this);

    // One line for the tray menu and the list: "21h 04m 29s", plus the session when one is running.
    public string Summary => SessionRunning ? $"{Countdown}  ·  {Model.SessionHours}h: {Format.Compact(SessionLeft)}" : Countdown;

    public void Refresh(DateTime at)
    {
        now = at;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}

public class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
