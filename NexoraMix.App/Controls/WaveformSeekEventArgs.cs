using System.Windows;

namespace NexoraMix.App.Controls;

public sealed class WaveformSeekEventArgs : RoutedEventArgs
{
    public WaveformSeekEventArgs(RoutedEvent routedEvent, object source, double positionSeconds, bool setCue)
        : base(routedEvent, source)
    {
        PositionSeconds = positionSeconds;
        SetCue = setCue;
    }

    public double PositionSeconds { get; }
    public bool SetCue { get; }
}
