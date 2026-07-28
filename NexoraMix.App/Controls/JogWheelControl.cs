using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NexoraMix.App.Controls;

public sealed class JogWheelControl : FrameworkElement
{
    private bool _isDragging;
    private double _lastPointerAngle;

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying), typeof(bool), typeof(JogWheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BpmProperty = DependencyProperty.Register(
        nameof(Bpm), typeof(double), typeof(JogWheelControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionSecondsProperty = DependencyProperty.Register(
        nameof(PositionSeconds), typeof(double), typeof(JogWheelControl),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DeckLabelProperty = DependencyProperty.Register(
        nameof(DeckLabel), typeof(string), typeof(JogWheelControl),
        new FrameworkPropertyMetadata("A", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(JogWheelControl),
        new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public double Bpm
    {
        get => (double)GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public double PositionSeconds
    {
        get => (double)GetValue(PositionSecondsProperty);
        set => SetValue(PositionSecondsProperty, value);
    }

    public string DeckLabel
    {
        get => (string)GetValue(DeckLabelProperty);
        set => SetValue(DeckLabelProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public event EventHandler<JogDeltaEventArgs>? Jogged;

    public JogWheelControl()
    {
        MinWidth = 150;
        MinHeight = 150;
        Focusable = true;
        Cursor = Cursors.Hand;
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) => CompositionTarget.Rendering -= OnRendering;
        MouseLeftButtonDown += OnPointerDown;
        MouseMove += OnPointerMove;
        MouseLeftButtonUp += OnPointerUp;
        MouseLeave += (_, _) => EndDrag();
        TouchDown += OnTouchDown;
        TouchMove += OnTouchMove;
        TouchUp += (_, _) => EndDrag();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var size = Math.Max(40d, Math.Min(ActualWidth, ActualHeight));
        var center = new Point(ActualWidth / 2d, ActualHeight / 2d);
        var radius = size / 2d - 5d;

        drawingContext.DrawEllipse(
            new RadialGradientBrush(Color.FromRgb(36, 43, 56), Color.FromRgb(4, 7, 12)),
            new Pen(new SolidColorBrush(Color.FromRgb(71, 86, 111)), 2d),
            center,
            radius,
            radius);

        for (var i = 1; i <= 11; i++)
        {
            var groove = radius * (0.25d + i * 0.055d);
            var alpha = (byte)Math.Clamp(22 + i * 3, 0, 75);
            drawingContext.DrawEllipse(
                null,
                new Pen(new SolidColorBrush(Color.FromArgb(alpha, 205, 220, 245)), 0.7d),
                center,
                groove,
                groove);
        }

        var labelRadius = radius * 0.47d;
        drawingContext.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(12, 17, 27)),
            new Pen(AccentBrush, 2d),
            center,
            labelRadius,
            labelRadius);

        var angle = GetRotationAngle();
        drawingContext.PushTransform(new RotateTransform(angle, center.X, center.Y));
        drawingContext.DrawRoundedRectangle(
            AccentBrush,
            null,
            new Rect(center.X - 3d, center.Y - radius + 14d, 6d, 24d),
            3d,
            3d);
        drawingContext.DrawEllipse(
            AccentBrush,
            null,
            new Point(center.X, center.Y - labelRadius + 13d),
            5d,
            5d);
        drawingContext.Pop();

        var deckText = new FormattedText(
            $"DECK {DeckLabel}",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Display"),
            Math.Max(14d, radius * 0.16d),
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(deckText, new Point(center.X - deckText.Width / 2d, center.Y - deckText.Height / 2d - 7d));

        var statusText = new FormattedText(
            IsPlaying ? "RUNNING" : "PAUSED",
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable Text"),
            Math.Max(9d, radius * 0.08d),
            IsPlaying ? AccentBrush : new SolidColorBrush(Color.FromRgb(132, 147, 170)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(statusText, new Point(center.X - statusText.Width / 2d, center.Y + 16d));
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (IsPlaying) InvalidateVisual();
    }

    private double GetRotationAngle()
    {
        var tempoFactor = Bpm > 0d ? Math.Clamp(Bpm / 120d, 0.55d, 1.75d) : 1d;
        const double baseRpm = 33.333333d;
        return (PositionSeconds * baseRpm * tempoFactor * 6d) % 360d;
    }

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        _isDragging = true;
        _lastPointerAngle = PointerAngle(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed) return;
        ApplyPointer(e.GetPosition(this));
        e.Handled = true;
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        CaptureTouch(e.TouchDevice);
        _isDragging = true;
        _lastPointerAngle = PointerAngle(e.GetTouchPoint(this).Position);
        e.Handled = true;
    }

    private void OnTouchMove(object? sender, TouchEventArgs e)
    {
        if (!_isDragging) return;
        ApplyPointer(e.GetTouchPoint(this).Position);
        e.Handled = true;
    }

    private void ApplyPointer(Point point)
    {
        var current = PointerAngle(point);
        var delta = current - _lastPointerAngle;
        if (delta > 180d) delta -= 360d;
        if (delta < -180d) delta += 360d;
        _lastPointerAngle = current;

        // Un giro equivale a circa 1,8 secondi: sufficiente per cue/nudge remoto e preparazione.
        var seconds = Math.Clamp(delta / 360d * 1.8d, -0.25d, 0.25d);
        Jogged?.Invoke(this, new JogDeltaEventArgs(seconds));
    }

    private double PointerAngle(Point point)
    {
        var x = point.X - ActualWidth / 2d;
        var y = point.Y - ActualHeight / 2d;
        return Math.Atan2(y, x) * 180d / Math.PI;
    }

    private void EndDrag()
    {
        _isDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        foreach (var touch in TouchesCaptured.ToArray()) ReleaseTouchCapture(touch);
    }
}

public sealed class JogDeltaEventArgs : EventArgs
{
    public JogDeltaEventArgs(double deltaSeconds) => DeltaSeconds = deltaSeconds;
    public double DeltaSeconds { get; }
}
