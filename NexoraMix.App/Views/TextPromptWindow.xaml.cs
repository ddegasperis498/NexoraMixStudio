using System.Windows;

namespace NexoraMix.App.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string message, string initialValue = "")
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ValueTextBox.Text = initialValue;

        if (title.Contains("Impostazioni Spotify", StringComparison.OrdinalIgnoreCase))
        {
            FieldLabelText.Text = "CLIENT ID SPOTIFY";
            ExampleText.Text = "Incolla il Client ID mostrato nel Spotify Developer Dashboard. Nel dashboard registra esattamente il Redirect URI http://127.0.0.1:5543/callback/. Non inserire Client Secret, password o link di brani.";
        }
        else if (title.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            FieldLabelText.Text = "LINK SPOTIFY";
            ExampleText.Text = "Incolla un link pubblico completo: https://open.spotify.com/track/... oppure /album/... oppure /playlist/.... I metadati verranno importati; per analisi BPM e mix serve il file audio locale.";
        }
        else
        {
            FieldLabelText.Text = "VALORE";
            ExampleText.Text = string.Empty;
        }

        Loaded += (_, _) =>
        {
            ValueTextBox.Focus();
            ValueTextBox.SelectAll();
        };
    }

    public string Value => ValueTextBox.Text;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
