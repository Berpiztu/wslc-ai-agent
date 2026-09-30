namespace WslcAgent.Tray;

/// <summary>
/// The icon beside the clock, one per agent in a user's session: the
/// installed agent's, at the port the installer wrote, and a development
/// agent's beside it when started with <c>--agent http://127.0.0.1:8070/</c>
/// (start-agent.ps1 -Tray), so notifications are tried without installing.
/// </summary>
internal static class Program
{
    private const string AgentOption = "--agent";

    [STAThread]
    private static void Main(string[] args)
    {
        var given = Given(args);
        var agent = given ?? AgentAddress.Read();
        using var single = new Mutex(initiallyOwned: true, $@"Local\WSLC-AI-Agent-Tray-{agent.Port}", out var first);
        if (!first)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new AgentTray(agent, named: given is not null));
    }

    /// <summary>The agent named on the command line, or null to read the installed one's.</summary>
    private static Uri? Given(string[] args)
    {
        var at = Array.IndexOf(args, AgentOption);
        return at >= 0 && at + 1 < args.Length && Uri.TryCreate(args[at + 1], UriKind.Absolute, out var agent) ? agent : null;
    }
}
