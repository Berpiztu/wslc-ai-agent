namespace WslcAgent.App;

/// <summary>
/// The agent this client talks to, resolved once at start-up. In order: the
/// <c>--agent-url</c> command-line argument (debug scripts), the app preference
/// (the agent chosen in Settings → This client; the Android <c>agent_url</c>
/// intent extra writes it too), the address the Windows installer stored under
/// HKCU, then the agent on this machine.
/// </summary>
internal static class AgentAddress
{
    /// <summary>Preference key holding the agent base URL.</summary>
    public const string Preference = "agent.url";

    /// <summary>The agent installed on this machine.</summary>
    public const string Default = "http://127.0.0.1:8069/";

    private const string CommandLineOption = "--agent-url";

    /// <summary>The address <see cref="Resolve"/> chose for this run.</summary>
    public static string Current { get; private set; } = Default;

    public static string Resolve()
    {
        var fromCommandLine = FromCommandLine();
        if (fromCommandLine is not null)
        {
            Preferences.Default.Set(Preference, fromCommandLine);
            return Current = fromCommandLine;
        }

        return Current = Preferences.Default.Get<string?>(Preference, null) is { Length: > 0 } chosen && IsAbsolute(chosen)
            ? chosen
            : FromInstaller() ?? Default;
    }

    private static string? FromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], CommandLineOption, StringComparison.OrdinalIgnoreCase) && IsAbsolute(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    /// <summary>The <c>WSLC_AGENTURL</c> the client MSI wrote (see packaging/client-install/Package.wxs).</summary>
    private static string? FromInstaller()
    {
#if WINDOWS
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Berpiztu\wslc-agent\Client");
        return key?.GetValue("AgentUrl") is string url && IsAbsolute(url) ? url.Trim() : null;
#else
        return null;
#endif
    }

    private static bool IsAbsolute(string value) => Uri.TryCreate(value, UriKind.Absolute, out _);
}
