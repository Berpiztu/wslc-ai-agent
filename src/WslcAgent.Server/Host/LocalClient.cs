using System.Net;

namespace WslcAgent.Server.Host;

/// <summary>
/// Whether the caller is sitting at the agent's own machine. Actions that put
/// something on that desktop (a terminal window) are meaningless from another
/// device, so they are refused there rather than opening a window nobody sees.
/// </summary>
public static class LocalClient
{
    /// <summary>
    /// True for a loopback client that asked for a loopback address and did not
    /// arrive through a proxy. The address it asked for matters as much as the
    /// socket: the Android emulator reaches its host at <c>10.0.2.2</c>, which
    /// arrives as a loopback connection although the caller is a phone on
    /// another screen. A forwarded request may come from anywhere too,
    /// whatever address the last hop has.
    /// </summary>
    public static bool IsLocal(HttpContext http) =>
        !Forwarded(http)
        && http.Connection.RemoteIpAddress is { } address && IsLoopback(address)
        && AskedForLoopback(http.Request.Host.Host);

    /// <summary>The host in the URL: <c>localhost</c> or a loopback address, not a LAN one.</summary>
    private static bool AskedForLoopback(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(host.Trim('[', ']'), out var address) && IsLoopback(address));

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static bool Forwarded(HttpContext http) =>
        http.Request.Headers.ContainsKey("X-Forwarded-For") || http.Request.Headers.ContainsKey("X-Forwarded-Proto") || http.Request.Headers.ContainsKey("Forwarded");
}
