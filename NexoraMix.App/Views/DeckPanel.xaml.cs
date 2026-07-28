using System.Windows;
using System.Windows.Controls;
using NexoraMix.App.Controls;
using NexoraMix.App.ViewModels;

namespace NexoraMix.App.Views;

public partial class DeckPanel : UserControl
{
    public DeckPanel() => InitializeComponent();

    private MainViewModel? MainViewModel =>
        Window.GetWindow(this)?.DataContext as MainViewModel;

    private void LoadHere_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DeckViewModel deck)
            MainViewModel?.LoadSelectedToDeck(deck);
    }

    private void Deck_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Deck_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not DeckViewModel deck) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && MainViewModel is { } main)
            await main.ImportAndLoadFilePathsToDeckAsync(paths, deck);
        e.Handled = true;
    }

    private void Cut_Pressed(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not DeckViewModel deck) return;
        deck.PressCut();
        if (sender is UIElement element) element.CaptureMouse();
        e.Handled = true;
    }

    private void Cut_Released(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (DataContext is not DeckViewModel deck) return;
        deck.ReleaseCut();
        if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Cut_TouchDown(object sender, System.Windows.Input.TouchEventArgs e)
    {
        if (DataContext is not DeckViewModel deck) return;
        deck.PressCut();
        if (sender is UIElement element) element.CaptureTouch(e.TouchDevice);
        e.Handled = true;
    }

    private void Cut_TouchUp(object sender, System.Windows.Input.TouchEventArgs e)
    {
        if (DataContext is not DeckViewModel deck) return;
        deck.ReleaseCut();
        if (sender is UIElement element) element.ReleaseTouchCapture(e.TouchDevice);
        e.Handled = true;
    }

    private void Waveform_SeekRequested(object sender, WaveformSeekEventArgs e)
    {
        if (DataContext is not DeckViewModel deck) return;
        deck.Seek(e.PositionSeconds);
        if (e.SetCue) deck.SetCue(e.PositionSeconds);
    }

    private void JogWheel_Jogged(object? sender, JogDeltaEventArgs e)
    {
        if (DataContext is not DeckViewModel deck || !deck.HasLocalAudio) return;
        deck.Seek(Math.Clamp(deck.PositionSeconds + e.DeltaSeconds, 0d, deck.DurationSeconds));
    }

    private void LoopSize_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckViewModel deck || sender is not FrameworkElement element) return;
        if (int.TryParse(element.Tag?.ToString(), out var beats)) deck.LoopBeats = beats;
    }

    private void HotCue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckViewModel deck || sender is not FrameworkElement element) return;
        if (int.TryParse(element.Tag?.ToString(), out var index)) deck.TriggerOrSetHotCue(index);
    }

    private void EffectToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DeckViewModel deck || sender is not FrameworkElement element) return;
        switch (element.Tag?.ToString())
        {
            case "echo": deck.EchoMix = deck.EchoMix > 0.01d ? 0d : 0.45d; break;
            case "crush": deck.BitCrush = deck.BitCrush > 0.01d ? 0d : 0.35d; break;
            case "sat": deck.Saturation = deck.Saturation > 0.01d ? 0d : 0.35d; break;
            case "gate": deck.Gate = deck.Gate > 0.01d ? 0d : 0.45d; break;
            case "roll": deck.Roll = deck.Roll > 0.01d ? 0d : 0.5d; break;
            case "brake": deck.Brake = deck.Brake > 0.01d ? 0d : 0.55d; break;
        }
    }
}
