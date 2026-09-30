using WslcAgent.UI.Lifetime;

namespace WslcAgent.App;

/// <summary>
/// The client closing itself on request: the way out of the "waiting for the
/// agent" screen. On Windows the application quits; on Android the process
/// ends, as it does after a switch of agent (<see cref="MauiClientServers"/>),
/// because MAUI's Quit leaves the activity where it was.
/// </summary>
internal sealed class MauiClientLifetime : IClientLifetime
{
    public bool Supported => true;

    public void Close()
    {
#if ANDROID
        Java.Lang.JavaSystem.Exit(0);
#else
        Application.Current?.Quit();
#endif
    }
}
