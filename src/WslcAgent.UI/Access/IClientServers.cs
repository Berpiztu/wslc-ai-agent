namespace WslcAgent.UI.Access;

/// <summary>
/// The agents a native client knows and the one it talks to: the agent on this
/// machine, one on the LAN, or the public address through the internet. The
/// browser has none of this: it is always served by the agent it talks to.
/// </summary>
public interface IClientServers
{
    /// <summary>False in the browser.</summary>
    bool Supported { get; }

    /// <summary>The agent this client talks to now.</summary>
    string Current { get; }

    /// <summary>The saved agents, the current one among them.</summary>
    IReadOnlyList<string> Saved { get; }

    /// <summary>Saves an agent address (<c>http://</c> added when none is typed); the address as saved.</summary>
    string Add(string address);

    /// <summary>Forgets a saved agent; the current one stays.</summary>
    void Remove(string address);

    /// <summary>Makes <paramref name="address"/> the current agent and restarts the client on it.</summary>
    void SwitchTo(string address);
}

/// <summary>The browser's: a single agent, the one serving the page.</summary>
public sealed class NoClientServers : IClientServers
{
    public bool Supported => false;

    public string Current => "";

    public IReadOnlyList<string> Saved => [];

    public string Add(string address) => throw new NotSupportedException();

    public void Remove(string address) => throw new NotSupportedException();

    public void SwitchTo(string address) => throw new NotSupportedException();
}

/// <summary>How a typed agent address is kept: absolute http(s), a trailing slash, no path beyond the root.</summary>
public static class AgentAddresses
{
    public static string Normalize(string address)
    {
        var text = address.Trim();
        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0)
        {
            throw new ArgumentException($"Not an agent address: {address}. Example: https://agent.example.com or http://192.168.1.10:8069", nameof(address));
        }

        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }
}
