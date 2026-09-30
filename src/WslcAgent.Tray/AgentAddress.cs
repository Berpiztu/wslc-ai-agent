using Microsoft.Win32;

namespace WslcAgent.Tray;

/// <summary>
/// Where the agent of this user answers on this machine: the port the
/// installer wrote (HKCU\Software\Berpiztu\wslc-agent\Agent, as the agent and
/// its logon task read it), on the loopback, whatever host it binds to.
/// </summary>
internal static class AgentAddress
{
    private const string Key = @"Software\Berpiztu\wslc-agent\Agent";

    private const int DefaultPort = 8069;

    public static Uri Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        var port = int.TryParse(key?.GetValue("Port")?.ToString(), out var written) && written is > 0 and < 65536 ? written : DefaultPort;
        return new Uri($"http://127.0.0.1:{port}/");
    }
}
