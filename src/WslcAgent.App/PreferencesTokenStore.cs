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
    private static string Key => "agent.session:" + AgentAddress.Current;

    public string? Load() => Preferences.Default.Get<string?>(Key, null);

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
