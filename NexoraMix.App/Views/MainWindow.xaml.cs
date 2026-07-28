using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using NexoraMix.App.ViewModels;

namespace NexoraMix.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        RestoreWindowPlacement();
    }

    private void RestoreWindowPlacement()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(_viewModel.SavedWindowWidth, MinWidth, Math.Max(MinWidth, workArea.Width));
        Height = Math.Clamp(_viewModel.SavedWindowHeight, MinHeight, Math.Max(MinHeight, workArea.Height));

        if (_viewModel.SavedWindowLeft is double left && _viewModel.SavedWindowTop is double top)
        {
            Left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - MinWidth));
            Top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - MinHeight));
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (_viewModel.SavedWindowMaximized)
            WindowState = System.Windows.WindowState.Maximized;
    }

    private void LibraryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.LoadSelectedToAvailableDeck();

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await _viewModel.ImportFilePathsAsync(paths);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (control && e.Key == Key.O)
        {
            Execute(_viewModel.ImportFilesCommand);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.S)
        {
            Execute(_viewModel.SaveSessionCommand);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.R)
        {
            Execute(_viewModel.StartStopRecordingCommand);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                _viewModel.ToggleMasterPlayPause();
                e.Handled = true;
                break;
            case Key.Q:
                Execute(_viewModel.DeckA.CueCommand);
                e.Handled = true;
                break;
            case Key.P:
                Execute(_viewModel.DeckB.CueCommand);
                e.Handled = true;
                break;
            case Key.Z:
                Execute(_viewModel.DeckC.CueCommand);
                e.Handled = true;
                break;
            case Key.M:
                Execute(_viewModel.DeckD.CueCommand);
                e.Handled = true;
                break;
            case Key.S:
                _viewModel.ToggleFollowerSync();
                e.Handled = true;
                break;
            case Key.F6:
                Execute(_viewModel.StartAutoMashupCommand);
                e.Handled = true;
                break;
            case Key.Escape:
                Execute(_viewModel.StopAllCommand);
                e.Handled = true;
                break;
        }
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == System.Windows.WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _viewModel.SaveWindowPlacement(
            bounds.Width,
            bounds.Height,
            bounds.Left,
            bounds.Top,
            WindowState == System.Windows.WindowState.Maximized);
        _viewModel.Dispose();
    }
}
