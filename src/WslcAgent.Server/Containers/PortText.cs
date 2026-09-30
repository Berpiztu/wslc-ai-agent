namespace WslcAgent.Server.Containers;

/// <summary>
/// The one way a published port is shown: <c>hostPort->containerPort</c>
/// (no bind address, no protocol); the UI draws the
/// arrow.
/// </summary>
internal static class PortText
{
    public static string Display(string hostPort, string containerPort) =>
        hostPort.Length > 0 && containerPort.Length > 0 ? $"{PortOnly(hostPort)}->{PortOnly(containerPort)}"
        : hostPort.Length > 0 ? PortOnly(hostPort)
        : PortOnly(containerPort);

    /// <summary>From WSLC's rendered form (<c>0.0.0.0:8080->80/tcp</c>, <c>:::8080->80/tcp</c>, <c>80/tcp</c>).</summary>
    public static string Display(string rendered)
    {
        var arrow = rendered.IndexOf("->", StringComparison.Ordinal);
        return arrow < 0 ? Display("", rendered) : Display(rendered[..arrow], rendered[(arrow + 2)..]);
    }

    /// <summary>The port number out of <c>ip:port</c>, <c>[::]:port</c>, <c>port/proto</c>.</summary>
    private static string PortOnly(string text)
    {
        var slash = text.IndexOf('/');
        var value = slash < 0 ? text : text[..slash];
        var colon = value.LastIndexOf(':');
        return (colon < 0 ? value : value[(colon + 1)..]).Trim();
    }
}
