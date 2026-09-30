namespace WslcAgent.UI.Access;

/// <summary>
/// Where a client keeps the session it signed in with between runs. The browser
/// keeps nothing here: the agent's session cookie already does that. A native
/// client stores it in its preferences.
/// </summary>
public interface IAgentTokenStore
{
    string? Load();

    /// <summary>Stores the session, or forgets it with null (Log out).</summary>
    void Save(string? token);
}

/// <summary>The browser's store: the cookie is the session, so there is nothing to keep.</summary>
public sealed class CookieTokenStore : IAgentTokenStore
{
    public string? Load() => null;

    public void Save(string? token)
    {
    }
}
