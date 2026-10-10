using Microsoft.Win32;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// Where the agent to talk to answers. The one that was named (<c>--agent</c>,
/// or WSLC_COMPOSE_AGENT); without one, this user's own on this machine: the
/// port its installer wrote (HKCU\Software\Berpiztu\wslc-agent\Agent, as the
/// agent, its logon task and its tray read it), on the loopback, where the
/// agent asks whoever sits at its machine for no login.
/// </summary>
internal static class AgentAddress
{
    public const string Variable = "WSLC_COMPOSE_AGENT";

    private const string Key = @"Software\Berpiztu\wslc-agent\Agent";

    private const int DefaultPort = 8069;

    public static Uri Of(string named)
    {
        var given = (named.Length > 0 ? named : Environment.GetEnvironmentVariable(Variable) ?? "").Trim();
        if (given.Length == 0)
        {
            return new Uri($"http://127.0.0.1:{InstalledPort()}/");
        }

        // Written as an address is said, "pc1:8069": the scheme is added, and the bar the calls' paths hang from.
        var whole = given.Contains("://", StringComparison.Ordinal) ? given : $"http://{given}";
        return Uri.TryCreate(whole.EndsWith('/') ? whole : $"{whole}/", UriKind.Absolute, out var address) && address.Scheme is "http" or "https"
            ? address
            : throw new UsageException($"Not an agent's address: {given}");
    }

    private static int InstalledPort()
    {
        if (!OperatingSystem.IsWindows())
        {
            return DefaultPort;
        }

        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return int.TryParse(key?.GetValue("Port")?.ToString(), out var written) && written is > 0 and < 65536 ? written : DefaultPort;
    }
}
