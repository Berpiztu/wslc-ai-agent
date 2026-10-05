using WslcAgent.UI.Access;

namespace WslcAgent.App;

/// <summary>
/// The native client's session, kept in the app preferences so a restart does
/// not ask to sign in again, one per agent. It is a signed session that ends after seven days
/// or when the password changes, never the password itself. (Secure storage is
/// not available to the unpackaged Windows client.)
/// </summary>
internal sealed class PreferencesTokenStore : IAgentTokenStore
{
    /// <summary>One session per agent: switching agents in Settings does not send one agent's session to another.</summary>
    private static string Key => KeyOf(AgentAddress.Current);

    public string? Load() => Preferences.Default.Get<string?>(Key, null);

    /// <summary>The session kept for another agent than the current one: a notification's button acts on the agent it came from.</summary>
    public static string? LoadFor(string agent) => Preferences.Default.Get<string?>(KeyOf(agent), null);

    private static string KeyOf(string agent) => "agent.session:" + agent;

    public void Save(string? token)
    {
        if (token is null)
        {
            Preferences.Default.Remove(Key);
            return;
        }

        Preferences.Default.Set(Key, token);
    }
}
