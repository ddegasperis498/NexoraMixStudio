using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace NexoraMix.TabletClient;

[Activity(
    Label = "Nexora Mix Tablet",
    MainLauncher = true,
    Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.ScreenSize |
                           ConfigChanges.Orientation |
                           ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : Activity
{
    private const string PreferencesName = "nexora_mix_tablet";
    private const string UrlPreference = "pc_url";

    private WebView? _webView;
    private LinearLayout? _connectionPanel;
    private EditText? _urlInput;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Kitkat && Window?.DecorView is View decorView)
        {
#pragma warning disable CA1422
            decorView.SystemUiFlags = SystemUiFlags.Fullscreen |
                                      SystemUiFlags.HideNavigation |
                                      SystemUiFlags.ImmersiveSticky;
#pragma warning restore CA1422
        }

        var root = new FrameLayout(this) { Background = new ColorDrawable(Color.Rgb(5, 8, 13)) };
        _webView = CreateWebView();
        root.AddView(_webView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        _connectionPanel = CreateConnectionPanel();
        var panelLayout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Top
        };
        root.AddView(_connectionPanel, panelLayout);

        SetContentView(root);

        var savedUrl = GetSharedPreferences(PreferencesName, FileCreationMode.Private)?.GetString(UrlPreference, string.Empty) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(savedUrl))
        {
            _urlInput!.Text = savedUrl;
            LoadConsole(savedUrl);
        }
    }

    private WebView CreateWebView()
    {
        var webView = new WebView(this);
        webView.SetBackgroundColor(Color.Rgb(5, 8, 13));
        webView.SetWebViewClient(new WebViewClient());
        webView.Settings.JavaScriptEnabled = true;
        webView.Settings.DomStorageEnabled = true;
        webView.Settings.DatabaseEnabled = true;
        webView.Settings.MediaPlaybackRequiresUserGesture = false;
        webView.Settings.SetSupportZoom(false);
        webView.Settings.BuiltInZoomControls = false;
        webView.Settings.DisplayZoomControls = false;
        webView.LongClickable = false;
        webView.HapticFeedbackEnabled = false;
        webView.SetOnLongClickListener(new SuppressLongClickListener());
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            webView.Settings.MixedContentMode = MixedContentHandling.AlwaysAllow;
        return webView;
    }

    private LinearLayout CreateConnectionPanel()
    {
        var panel = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        panel.SetGravity(GravityFlags.CenterVertical);
        panel.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        panel.SetBackgroundColor(Color.Argb(245, 9, 16, 26));

        _urlInput = new EditText(this)
        {
            Hint = "http://192.168.1.10:17840/",
            TextSize = 16f,
            InputType = global::Android.Text.InputTypes.ClassText | global::Android.Text.InputTypes.TextVariationUri
        };
        _urlInput.SetSingleLine(true);
        _urlInput.SetTextColor(Color.White);
        _urlInput.SetHintTextColor(Color.Rgb(130, 145, 168));
        panel.AddView(_urlInput, new LinearLayout.LayoutParams(0, Dp(54), 1f));

        var connect = CreateButton("COLLEGA", Color.Rgb(37, 229, 196), Color.Rgb(3, 18, 15));
        connect.Click += (_, _) => LoadConsole(_urlInput.Text ?? string.Empty);
        panel.AddView(connect, new LinearLayout.LayoutParams(Dp(130), Dp(54)) { LeftMargin = Dp(8) });

        var settings = CreateButton("INDIRIZZO", Color.Rgb(23, 36, 56), Color.White);
        settings.Click += (_, _) => _connectionPanel!.Visibility = _connectionPanel.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible;
        panel.AddView(settings, new LinearLayout.LayoutParams(Dp(130), Dp(54)) { LeftMargin = Dp(8) });

        return panel;
    }

    private Button CreateButton(string text, Color background, Color foreground)
    {
        var button = new Button(this)
        {
            Text = text,
            TextSize = 13f
        };
        button.SetAllCaps(false);
        button.SetTextColor(foreground);
        button.SetBackgroundColor(background);
        return button;
    }

    private void LoadConsole(string rawUrl)
    {
        var url = NormalizeUrl(rawUrl);
        if (url is null)
        {
            Toast.MakeText(this, "Inserisci l'indirizzo mostrato dal programma sul PC.", ToastLength.Long)?.Show();
            return;
        }

        GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?.Edit()
            ?.PutString(UrlPreference, url)
            ?.Apply();

        _webView?.LoadUrl(url);
        _connectionPanel!.Visibility = ViewStates.Gone;
    }

    private static string? NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            candidate = "http://" + candidate;

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

    public override void OnBackPressed()
    {
        if (_connectionPanel?.Visibility == ViewStates.Gone)
        {
            _connectionPanel.Visibility = ViewStates.Visible;
            return;
        }

        if (_webView?.CanGoBack() == true)
        {
            _webView.GoBack();
            return;
        }

#pragma warning disable CA1422
        base.OnBackPressed();
#pragma warning restore CA1422
    }

    protected override void OnDestroy()
    {
        _webView?.StopLoading();
        _webView?.Destroy();
        _webView = null;
        base.OnDestroy();
    }

    private int Dp(int value) => (int)Math.Round(value * Resources!.DisplayMetrics!.Density);

    private sealed class SuppressLongClickListener : Java.Lang.Object, View.IOnLongClickListener
    {
        public bool OnLongClick(View? v) => true;
    }

}
