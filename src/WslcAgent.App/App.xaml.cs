namespace WslcAgent.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
#if ANDROID
        // With the window insets owned by MainActivity, resize keeps the last
        // fields above the virtual keyboard instead of panning the page.
        Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application.SetWindowSoftInputModeAdjust(
            this,
            Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust.Resize);
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "WSLC AI Client" };
#if WINDOWS
        // Opens maximized: the dashboard and the lists are laid out for the
        // whole screen. The user can restore it like any other window.
        window.Created += (_, _) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window { AppWindow.Presenter: Microsoft.UI.Windowing.OverlappedPresenter presenter })
            {
                presenter.Maximize();
            }
        };
#endif
        return window;
    }
}
