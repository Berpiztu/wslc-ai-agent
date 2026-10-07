using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Core.View;

namespace WslcAgent.App;

// The activity is single top (a new intent reaches
// OnNewIntent instead of a second instance), resizeable, and it survives
// rotation, split screen and keyboard changes without being recreated.
// A stable activity name so tooling can start the app explicitly:
//   adb shell am start -n ai.berpiztu.wslcaiagent/ai.berpiztu.wslcaiagent.MainActivity --es agent_url http://10.0.2.2:8070/
// The optional agent_url extra seeds the agent address (debug-android.ps1 uses it).
[Activity(
    Name = "ai.berpiztu.wslcaiagent.MainActivity",
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ResizeableActivity = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density
        | ConfigChanges.FontScale
        | ConfigChanges.LayoutDirection
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.Locale
        | ConfigChanges.ColorMode)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>The page background, behind the status and navigation bars.</summary>
    private static readonly Android.Graphics.Color SystemBarsGround = Android.Graphics.Color.ParseColor("#0f172a");

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        StoreAgentUrl(Intent);
        // Never restore Android's saved fragments: MAUI's root fragment comes
        // back before the view it lives in exists and the app dies with
        // "No view found for id … jumpToStart". One crash then saves that state
        // and every later launch repeats it. The app has nothing to restore —
        // the UI is a WebView that reloads itself — so it starts clean.
        base.OnCreate(null);
        KeepOutOfSystemBars();
        OnBackPressedDispatcher.AddCallback(this, new WebViewBackCallback(this));
        // A tapped notification that started the app: its page waits for the UI.
        AndroidClientNotifications.Open(Intent);
    }

    /// <summary>
    /// The back key goes to the one back handler (<see cref="WebViewBackCallback"/>)
    /// whoever has the focus. BlazorWebView answers the key itself while it
    /// has it — after any tap on the page — walking its own history with
    /// GoBack: past the dialog on top, past wslcAgent.back, a screen or several
    /// at once. Measured on the emulator: Images, View &amp; edit open, back
    /// landed on Containers with the dialog gone, and back() never called.
    /// The keyboard on screen still closes first: Android hands it the key
    /// before the activity.
    /// </summary>
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e?.KeyCode != Keycode.Back)
        {
            return base.DispatchKeyEvent(e);
        }

        if (e.Action == KeyEventActions.Up && !e.IsCanceled)
        {
            OnBackPressedDispatcher.OnBackPressed();
        }

        return true;
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        StoreAgentUrl(intent);
        AndroidClientNotifications.Open(intent);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        // Save As on the device (Export JSON): the SAF picker answers here.
        if (DocumentSaver.HandleActivityResult(requestCode, resultCode, data))
        {
            return;
        }

        base.OnActivityResult(requestCode, resultCode, data);
    }

    /// <summary>
    /// Back (button or gesture) asks the UI, which walks back:
    /// an open dialog, then the navigation drawer over the page,
    /// then the step before. Only when the UI says there is nothing left does
    /// the app close.
    /// <para>
    /// It cannot use the web view's own history: the UI navigates inside a
    /// single document, so <c>CanGoBack</c> is false however deep the user is,
    /// and back closed the app from the first screen on — with the drawer open,
    /// it closed the app instead of the drawer.
    /// </para>
    /// </summary>
    private sealed class WebViewBackCallback(MainActivity activity) : AndroidX.Activity.OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            var webView = FindWebView(activity.FindViewById(Android.Resource.Id.Content));
            if (webView is null)
            {
                Leave();
                return;
            }

            webView.EvaluateJavascript("window.wslcAgent && window.wslcAgent.back()", new BackAnswer(this));
        }

        /// <summary>Hands the gesture back to Android, which closes the activity.</summary>
        public void Leave()
        {
            Enabled = false;
            activity.OnBackPressedDispatcher.OnBackPressed();
            Enabled = true;
        }

        /// <summary>The UI answers "true" when it handled it; anything else means there was nothing left to go back to.</summary>
        private sealed class BackAnswer(WebViewBackCallback callback) : Java.Lang.Object, Android.Webkit.IValueCallback
        {
            public void OnReceiveValue(Java.Lang.Object? value)
            {
                if (value?.ToString() != "true")
                {
                    callback.Leave();
                }
            }
        }
    }

    private static Android.Webkit.WebView? FindWebView(Android.Views.View? view)
    {
        if (view is Android.Webkit.WebView webView)
        {
            return webView;
        }

        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                if (FindWebView(group.GetChildAt(i)) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Recent Android targets draw edge to edge: the web view would sit under
    /// the status bar (clock, battery) and the app bar's menu button would be
    /// swallowed by the system. The window owns the insets instead: the content
    /// view is padded by the system bars, the display cutout and the on-screen
    /// keyboard, and painted in the page ground so the strip matches the UI,
    /// with light bar icons so the clock stays readable on it.
    /// </summary>
    private void KeepOutOfSystemBars()
    {
        var content = FindViewById(Android.Resource.Id.Content);
        if (content is null)
        {
            return;
        }

        content.SetBackgroundColor(SystemBarsGround);
        ViewCompat.SetOnApplyWindowInsetsListener(content, new SafeAreaInsetListener());

        // The bars are drawn over our dark ground: light clock and icons, or
        // the status bar reads as empty.
        if (Window is not null)
        {
            var bars = new WindowInsetsControllerCompat(Window, content);
            bars.AppearanceLightStatusBars = false;
            bars.AppearanceLightNavigationBars = false;
        }
    }

    private static void StoreAgentUrl(Intent? intent)
    {
        var url = intent?.GetStringExtra("agent_url");
        if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Preferences.Default.Set(AgentAddress.Preference, url);
        }
    }

    private sealed class SafeAreaInsetListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat OnApplyWindowInsets(Android.Views.View? view, WindowInsetsCompat? insets)
        {
            var bars = insets?.GetInsets(
                WindowInsetsCompat.Type.SystemBars()
                | WindowInsetsCompat.Type.DisplayCutout()
                | WindowInsetsCompat.Type.Ime());
            if (view is not null && bars is not null)
            {
                view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }

            // Consumed here, where they are applied: passed on, the web view
            // took the keyboard's inset a second time — it shrank its own
            // visible area and scrolled the page over the padding — and when
            // the keyboard went the padding left but that scroll stayed, the
            // terminal shifted and the keyboard's space was left empty.
            return WindowInsetsCompat.Consumed!;
        }
    }
}
