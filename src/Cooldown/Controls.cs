using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Cooldown.Core;

namespace Cooldown;

// A thin rounded progress bar.
public class Bar : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = Prop<double>(nameof(Fraction), 0);
    public static readonly DependencyProperty FillProperty = Prop<Brush>(nameof(Fill), Brushes.White);
    public static readonly DependencyProperty TrackProperty = Prop<Brush>(nameof(Track), new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    public Bar() { Height = 4; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight, r = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);
        double fw = w * Math.Clamp(Fraction, 0, 1);
        if (fw >= h) dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, fw, h), r, r);
    }

    internal static DependencyProperty Prop<T>(string name, T fallback) =>
        DependencyProperty.Register(name, typeof(T), typeof(Bar),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));
}

// A progress ring, as on the collapsed island.
public class Ring : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(nameof(Fraction), typeof(double), typeof(Ring),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Ring),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(Ring),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double stroke = 2.5, d = Math.Min(ActualWidth, ActualHeight), r = d / 2 - stroke / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Track, stroke), c, r, r);
        double f = Math.Clamp(Fraction, 0, 1);
        if (f <= 0) return;
        var pen = new Pen(Fill, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (f >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        double a = f * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, pen, new PathGeometry(new[] { fig }));
    }
}

// Hour, minute and (in 12-hour mode) AM/PM dropdowns. WPF has no time picker of its own.
public class TimeInput : StackPanel
{
    readonly ComboBox hour = new() { MinWidth = 72 }, minute = new() { MinWidth = 72 }, ampm = new() { MinWidth = 76 };
    bool loading;
    TimeSpan value;

    public event Action<TimeSpan>? ValueChanged;

    // Read out by screen readers: "<Label> hour", "<Label> minute", "<Label> AM or PM".
    public string Label
    {
        get => label;
        set
        {
            label = value;
            System.Windows.Automation.AutomationProperties.SetName(hour, value + " hour");
            System.Windows.Automation.AutomationProperties.SetName(minute, value + " minute");
            System.Windows.Automation.AutomationProperties.SetName(ampm, value + " AM or PM");
        }
    }
    string label = "Time";

    public TimeInput()
    {
        Orientation = Orientation.Horizontal;
        for (int m = 0; m < 60; m++) minute.Items.Add(m.ToString("00"));
        ampm.Items.Add("AM");
        ampm.Items.Add("PM");
        Children.Add(hour);
        Children.Add(new TextBlock { Text = ":", Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        Children.Add(minute);
        ampm.Margin = new Thickness(8, 0, 0, 0);
        Children.Add(ampm);
        foreach (ComboBox c in new[] { hour, minute, ampm }) c.SelectionChanged += (_, _) => Changed();
        ApplyClockFormat();
    }

    public TimeSpan Value
    {
        get => value;
        set { this.value = new TimeSpan(value.Hours, value.Minutes, 0); Show(); }
    }

    // Rebuilds the hour list for the current 12- or 24-hour setting.
    public void ApplyClockFormat()
    {
        loading = true;
        hour.Items.Clear();
        if (Format.Use12h) for (int h = 1; h <= 12; h++) hour.Items.Add(h.ToString(CultureInfo.InvariantCulture));
        else for (int h = 0; h < 24; h++) hour.Items.Add(h.ToString("00"));
        ampm.Visibility = Format.Use12h ? Visibility.Visible : Visibility.Collapsed;
        loading = false;
        Show();
    }

    void Show()
    {
        loading = true;
        int h = value.Hours;
        hour.SelectedIndex = Format.Use12h ? (h % 12 == 0 ? 11 : h % 12 - 1) : h;
        minute.SelectedIndex = value.Minutes;
        ampm.SelectedIndex = h >= 12 ? 1 : 0;
        loading = false;
    }

    void Changed()
    {
        if (loading || hour.SelectedIndex < 0 || minute.SelectedIndex < 0) return;
        int h = Format.Use12h ? (hour.SelectedIndex + 1) % 12 + (ampm.SelectedIndex == 1 ? 12 : 0) : hour.SelectedIndex;
        value = new TimeSpan(h, minute.SelectedIndex, 0);
        ValueChanged?.Invoke(value);
    }
}

// Shows the "Change" hint on a hovered session, unless Undo has that spot: (hovered, showing Undo).
public class HoverHint : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [true, false] ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Eases to its content's new height instead of snapping, clipping the content as it goes: the
// island's 5-hour area, which swaps between the countdown and its editor. The rows below it slide
// along smoothly. Snaps when hidden, the first time, and when Windows' animation effects are off.
public class SmoothHeight : System.Windows.Controls.Decorator
{
    static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double), typeof(SmoothHeight),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsMeasure));

    double target = double.NaN;

    public SmoothHeight()
    {
        ClipToBounds = true;
        // Shown again later, it starts at its content's height rather than easing from a stale one.
        IsVisibleChanged += (_, e) => { if (e.NewValue is false) target = double.NaN; };
    }

    protected override Size MeasureOverride(Size available)
    {
        if (Child == null) return new Size();
        Child.Measure(new Size(available.Width, double.PositiveInfinity));
        double height = Child.DesiredSize.Height;
        if (height != target)
        {
            double shown = (double)GetValue(ShownProperty);
            bool snap = double.IsNaN(target) || double.IsNaN(shown) || !IsVisible || !SystemParameters.ClientAreaAnimation;
            target = height;
            if (snap)
            {
                BeginAnimation(ShownProperty, null);
                SetValue(ShownProperty, height);
            }
            else
            {
                BeginAnimation(ShownProperty, new System.Windows.Media.Animation.DoubleAnimation(shown, height, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                });
            }
        }
        double now = (double)GetValue(ShownProperty);
        return new Size(Child.DesiredSize.Width, double.IsNaN(now) ? height : Math.Round(now));
    }

    protected override Size ArrangeOverride(Size final)
    {
        Child?.Arrange(new Rect(0, 0, final.Width, Math.Max(final.Height, Child.DesiredSize.Height)));
        return final;
    }
}
