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
        EditCommand = new RelayCommand(BeginEdit);
        SaveCommand = new RelayCommand(() => { if (draft is { } end) app.ChangeSessionEnd(this, end); CancelEdit(); });
        CancelCommand = new RelayCommand(CancelEdit);
        EndNowCommand = new RelayCommand(() => { app.ChangeSessionEnd(this, null); CancelEdit(); });
        HourUpCommand = new RelayCommand(() => Step(TimeSpan.FromHours(1)));
        HourDownCommand = new RelayCommand(() => Step(TimeSpan.FromHours(-1)));
        MinuteUpCommand = new RelayCommand(() => Step(TimeSpan.FromMinutes(1)));
        MinuteDownCommand = new RelayCommand(() => Step(TimeSpan.FromMinutes(-1)));
    }

    public ICommand StartCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand EndNowCommand { get; }
    public ICommand HourUpCommand { get; }
    public ICommand HourDownCommand { get; }
    public ICommand MinuteUpCommand { get; }
    public ICommand MinuteDownCommand { get; }

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

    // ---- changing when the running session resets (the island's editor) ----

    DateTime? draft;  // the reset time being picked; null when not editing

    public bool Editing => draft.HasValue && SessionRunning;
    public bool SessionShown => SessionRunning && !Editing;
    public string DraftHour => draft?.ToString(Format.Use12h ? "%h" : "HH", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string DraftMinute => draft?.ToString("mm", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string DraftSuffix => Format.Use12h ? draft?.ToString("tt", System.Globalization.CultureInfo.InvariantCulture) ?? "" : "";
    public string DraftLeft => draft is { } d ? "in " + Format.Compact(d - now) : "";

    void BeginEdit()
    {
        if (Model.SessionEnd is not { } end) return;
        now = DateTime.Now;
        draft = Model.ClampSessionEnd(end, now);
        Changed();
    }

    public void CancelEdit()
    {
        if (draft == null) return;
        draft = null;
        Changed();
    }

    public void Step(TimeSpan by)
    {
        if (draft is not { } d) return;
        now = DateTime.Now;
        draft = Model.ClampSessionEnd(d + by, now);
        Changed();
    }

    public System.Collections.ObjectModel.ObservableCollection<Banked> Banked => Model.Banked;
    public bool HasBanked => Model.Banked.Count > 0;

    // "1 banked reset · expires in 2d 3h", "3 banked resets · next expires in 5h 10m"
    public string BankedText
    {
        get
        {
            int n = Model.Banked.Count;
            if (n == 0) return "";
            string left = Format.Compact(Model.Banked[0].Expires - now);
            return n == 1 ? "1 banked reset  ·  expires in " + left : n + " banked resets  ·  next expires in " + left;
        }
    }

    // One line for the tray menu and the list: "21h 04m 29s", plus the session when one is running.
    public string Summary => SessionRunning ? $"{Countdown}  ·  {Model.SessionHours}h: {Format.Compact(SessionLeft)}" : Countdown;

    public void Refresh(DateTime at)
    {
        now = at;
        if (draft is { } d) draft = Model.ClampSessionEnd(d, at);  // time moves on while picking
        Changed();
    }

    void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public class RelayCommand(Action run) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => run();
}
