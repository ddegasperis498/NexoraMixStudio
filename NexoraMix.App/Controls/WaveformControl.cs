using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NexoraMix.App.Controls;

public sealed class WaveformControl : FrameworkElement
{
    private const double MinimumZoom = 1d;
    private const double MaximumZoom = 64d;
    private double _zoom = 1d;
    private double _centerSeconds;
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(float[]), typeof(WaveformControl), new FrameworkPropertyMetadata(Array.Empty<float>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionSecondsProperty = DependencyProperty.Register(
        nameof(PositionSeconds), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BpmProperty = DependencyProperty.Register(
        nameof(Bpm), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BeatOffsetSecondsProperty = DependencyProperty.Register(
        nameof(BeatOffsetSeconds), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BeatsPerBarProperty = DependencyProperty.Register(
        nameof(BeatsPerBar), typeof(int), typeof(WaveformControl), new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CueInSecondsProperty = DependencyProperty.Register(
        nameof(CueInSeconds), typeof(double), typeof(WaveformControl), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WaveBrushProperty = DependencyProperty.Register(
        nameof(WaveBrush), typeof(Brush), typeof(WaveformControl), new FrameworkPropertyMetadata(Brushes.Turquoise, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly RoutedEvent SeekRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(SeekRequested), RoutingStrategy.Bubble, typeof(EventHandler<WaveformSeekEventArgs>), typeof(WaveformControl));

    public float[] Samples { get => (float[])GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double PositionSeconds { get => (double)GetValue(PositionSecondsProperty); set => SetValue(PositionSecondsProperty, value); }
    public double DurationSeconds { get => (double)GetValue(DurationSecondsProperty); set => SetValue(DurationSecondsProperty, value); }
    public double Bpm { get => (double)GetValue(BpmProperty); set => SetValue(BpmProperty, value); }
    public double BeatOffsetSeconds { get => (double)GetValue(BeatOffsetSecondsProperty); set => SetValue(BeatOffsetSecondsProperty, value); }
    public int BeatsPerBar { get => (int)GetValue(BeatsPerBarProperty); set => SetValue(BeatsPerBarProperty, value); }
    public double CueInSeconds { get => (double)GetValue(CueInSecondsProperty); set => SetValue(CueInSecondsProperty, value); }
    public Brush WaveBrush { get => (Brush)GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }

    public event EventHandler<WaveformSeekEventArgs> SeekRequested
    {
        add => AddHandler(SeekRequestedEvent, value);
        remove => RemoveHandler(SeekRequestedEvent, value);
    }

    public WaveformControl()
    {
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        Focusable = true;
        IsManipulationEnabled = true;
        ToolTip = "Click: posiziona · Rotella/pinch: zoom battute · Ctrl+Click: salva il punto di partenza";
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1) return;

        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(8, 11, 19)), new Pen(new SolidColorBrush(Color.FromRgb(43, 51, 72)), 1), new Rect(0.5, 0.5, width - 1, height - 1), 8, 8);
        var range = GetVisibleRange();
        DrawBeatGrid(dc, width, height, range.Start, range.Duration);
        DrawWaveform(dc, width, height, range.Start, range.Duration);
        DrawMarker(dc, CueInSeconds, range.Start, range.Duration, width, height, Brushes.Gold, 2, "CUE");
        DrawMarker(dc, PositionSeconds, range.Start, range.Duration, width, height, Brushes.White, 2, null);
    }

    private void DrawBeatGrid(DrawingContext dc, double width, double height, double visibleStart, double visibleDuration)
    {
        if (Bpm <= 0 || DurationSeconds <= 0 || visibleDuration <= 0) return;
        var beatLength = 60d / Bpm;
        var firstVisibleBeat = (long)Math.Ceiling((visibleStart - BeatOffsetSeconds) / beatLength);
        var beatsPerBar = Math.Max(1, BeatsPerBar);
        for (var beatIndex = firstVisibleBeat; ; beatIndex++)
        {
            var time = BeatOffsetSeconds + beatIndex * beatLength;
            if (time > visibleStart + visibleDuration) break;
            if (time < 0) continue;

            var x = (time - visibleStart) / visibleDuration * width;
            var strong = PositiveModulo(beatIndex, beatsPerBar) == 0;
            var color = strong ? Color.FromArgb(125, 124, 92, 255) : Color.FromArgb(38, 124, 92, 255);
            dc.DrawLine(new Pen(new SolidColorBrush(color), strong ? 1.4 : 0.7), new Point(x, 4), new Point(x, height - 4));

            var pixelsPerBar = beatLength * beatsPerBar / visibleDuration * width;
            if (strong && beatIndex >= 0 && pixelsPerBar >= 34d)
            {
                var barNumber = beatIndex / beatsPerBar + 1;
                var label = new FormattedText(
                    $"M{barNumber}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"),
                    8,
                    new SolidColorBrush(Color.FromArgb(185, 184, 169, 255)),
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(label, new Point(Math.Min(width - label.Width - 3, x + 3), height - label.Height - 4));
            }
        }
    }

    private void DrawWaveform(DrawingContext dc, double width, double height, double visibleStart, double visibleDuration)
    {
        var samples = Samples ?? Array.Empty<float>();
        if (samples.Length == 0)
        {
            var text = new FormattedText("Waveform disponibile dopo l’analisi", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, new SolidColorBrush(Color.FromRgb(113, 126, 149)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(Math.Max(10, (width - text.Width) / 2), Math.Max(8, (height - text.Height) / 2)));
            return;
        }

        var center = height / 2;
        var pen = new Pen(WaveBrush, Math.Max(1, width / Math.Max(1, samples.Length / _zoom) * 0.68));
        var firstSample = DurationSeconds > 0 ? (int)Math.Floor(visibleStart / DurationSeconds * samples.Length) : 0;
        var lastSample = DurationSeconds > 0 ? (int)Math.Ceiling((visibleStart + visibleDuration) / DurationSeconds * samples.Length) : samples.Length;
        firstSample = Math.Clamp(firstSample, 0, Math.Max(0, samples.Length - 1));
        lastSample = Math.Clamp(lastSample, firstSample + 1, samples.Length);
        var visibleSamples = lastSample - firstSample;
        var stride = Math.Max(1, (int)Math.Ceiling(visibleSamples / width));
        for (var i = firstSample; i < lastSample; i += stride)
        {
            var sampleTime = i / (double)Math.Max(1, samples.Length - 1) * DurationSeconds;
            var x = (sampleTime - visibleStart) / visibleDuration * width;
            var amplitude = Math.Clamp(samples[i], 0, 1) * (height * 0.42);
            dc.DrawLine(pen, new Point(x, center - amplitude), new Point(x, center + amplitude));
        }
    }

    private static void DrawMarker(DrawingContext dc, double seconds, double visibleStart, double visibleDuration, double width, double height, Brush brush, double thickness, string? label)
    {
        if (visibleDuration <= 0 || seconds < visibleStart || seconds > visibleStart + visibleDuration) return;
        var x = (seconds - visibleStart) / visibleDuration * width;
        dc.DrawLine(new Pen(brush, thickness), new Point(x, 2), new Point(x, height - 2));
        if (string.IsNullOrWhiteSpace(label)) return;
        var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), 9, brush, 1);
        dc.DrawText(text, new Point(Math.Min(width - text.Width - 4, x + 4), 4));
    }

    private static long PositiveModulo(long value, int modulo)
    {
        var result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (DurationSeconds <= 0 || ActualWidth <= 0) return;
        Focus();
        var range = GetVisibleRange();
        var ratio = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        var setCue = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        RaiseEvent(new WaveformSeekEventArgs(SeekRequestedEvent, this, range.Start + ratio * range.Duration, setCue));
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (DurationSeconds <= 0 || ActualWidth <= 0) return;
        var anchorRatio = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0, 1);
        ZoomAt(anchorRatio, e.Delta > 0 ? 1.25d : 0.80d);
        e.Handled = true;
    }

    protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
    {
        base.OnManipulationDelta(e);
        if (DurationSeconds <= 0 || ActualWidth <= 0) return;
        var scale = Math.Max(e.DeltaManipulation.Scale.X, e.DeltaManipulation.Scale.Y);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || Math.Abs(scale - 1d) < 0.01d) return;
        var origin = e.ManipulationOrigin;
        ZoomAt(Math.Clamp(origin.X / ActualWidth, 0, 1), scale);
        e.Handled = true;
    }

    private (double Start, double Duration) GetVisibleRange()
    {
        if (DurationSeconds <= 0) return (0d, 0d);
        if (_centerSeconds <= 0d || _centerSeconds > DurationSeconds) _centerSeconds = Math.Clamp(PositionSeconds, 0d, DurationSeconds);
        if (_zoom <= MinimumZoom + 0.001d) return (0d, DurationSeconds);

        var visibleDuration = Math.Max(0.05d, DurationSeconds / _zoom);
        var center = PositionSeconds >= 0 && Math.Abs(PositionSeconds - _centerSeconds) > visibleDuration * 0.55d
            ? PositionSeconds
            : _centerSeconds;
        _centerSeconds = Math.Clamp(center, visibleDuration / 2d, Math.Max(visibleDuration / 2d, DurationSeconds - visibleDuration / 2d));
        var start = Math.Clamp(_centerSeconds - visibleDuration / 2d, 0d, Math.Max(0d, DurationSeconds - visibleDuration));
        return (start, visibleDuration);
    }

    private void ZoomAt(double anchorRatio, double factor)
    {
        var before = GetVisibleRange();
        var anchorSeconds = before.Start + anchorRatio * before.Duration;
        _zoom = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);
        var afterDuration = DurationSeconds / _zoom;
        _centerSeconds = anchorSeconds + (0.5d - anchorRatio) * afterDuration;
        _centerSeconds = Math.Clamp(_centerSeconds, afterDuration / 2d, Math.Max(afterDuration / 2d, DurationSeconds - afterDuration / 2d));
        InvalidateVisual();
    }
}
