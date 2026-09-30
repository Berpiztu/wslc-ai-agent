using WslcAgent.UI.Access;

namespace WslcAgent.App;

/// <summary>
/// The saved agents in the app preferences, and the switch between them. The
/// client's whole UI and its HTTP client are built for one agent at start-up,
/// so switching stores the choice (<see cref="AgentAddress"/> reads it first)
/// and starts the app again on it.
/// </summary>
internal sealed class MauiClientServers : IClientServers
{
    private const string SavedKey = "agent.servers";

    public bool Supported => true;

    public string Current => AgentAddress.Current;

    public IReadOnlyList<string> Saved
    {
        get
        {
            var saved = Preferences.Default.Get(SavedKey, "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (!saved.Contains(Current, StringComparer.OrdinalIgnoreCase))
            {
                // Kept, not only shown: after switching away it stays in the list to come back to.
                saved.Insert(0, Current);
                Store(saved);
            }

            return saved;
        }
    }

    public string Add(string address)
    {
        var normalized = AgentAddresses.Normalize(address);
        var saved = Saved.ToList();
        if (!saved.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            saved.Add(normalized);
            Store(saved);
        }

        return normalized;
    }

    public void Remove(string address)
    {
        if (string.Equals(address, Current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Store(Saved.Where(s => !string.Equals(s, address, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    public void SwitchTo(string address)
    {
        var normalized = Add(address);
        Preferences.Default.Set(AgentAddress.Preference, normalized);
        Restart(normalized);
    }

    private static void Store(IEnumerable<string> saved) => Preferences.Default.Set(SavedKey, string.Join('\n', saved));

    /// <summary>A new process on Windows; on Android the launcher activity again in a fresh task, then this process ends.</summary>
    private static void Restart(string address)
    {
#if WINDOWS
        if (Environment.ProcessPath is { } path)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = false });
        }

        Application.Current?.Quit();
#elif ANDROID
        var context = Android.App.Application.Context;
        // MAUI writes preferences to disk asynchronously; the process ends below, so the
        // choice is written synchronously first or the app comes back on the old agent.
        context.GetSharedPreferences($"{context.PackageName}_preferences", Android.Content.FileCreationMode.Private)?
            .Edit()?.PutString(AgentAddress.Preference, address)?.PutString(SavedKey, Preferences.Default.Get(SavedKey, ""))?.Commit();
        if (context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!) is { } intent)
        {
            intent.AddFlags(Android.Content.ActivityFlags.NewTask | Android.Content.ActivityFlags.ClearTask);
            context.StartActivity(intent);
        }

        Java.Lang.JavaSystem.Exit(0);
#endif
    }
}
