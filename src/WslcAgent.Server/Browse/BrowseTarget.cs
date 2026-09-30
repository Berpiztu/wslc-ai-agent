using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using WslcAgent.Server.Containers;

namespace WslcAgent.Server.Browse;

/// <summary>
/// The rules that keep the host browser on what it was opened for: the one
/// published loopback port of the one container, never the agent itself, and
/// the sizes and ids a pane may ask for.
/// </summary>
public static partial class BrowseTarget
{
    public const int MinWidth = 320;
    public const int MinHeight = 240;
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;

    /// <summary>Bind addresses that only the host itself reaches (empty is every address, as inspect writes it).</summary>
    private static readonly HashSet<string> LoopbackBinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "0.0.0.0", "::", "[::]", "127.0.0.1", "localhost", "::1", "[::1]",
    };

    public static int Width(int? value, int fallback) => Math.Clamp(value is > 0 ? value.Value : fallback, MinWidth, MaxWidth);

    public static int Height(int? value, int fallback) => Math.Clamp(value is > 0 ? value.Value : fallback, MinHeight, MaxHeight);

    /// <summary><c>http://127.0.0.1:{hostPort}/</c> when the container publishes that port on loopback; otherwise the reason it is refused.</summary>
    public static string Resolve(IReadOnlyList<ContainerInspection.PortBinding> binds, string hostPort, int agentPort)
    {
        var port = hostPort.Trim();
        if (!int.TryParse(port, out var number) || number is < 1 or > 65535 || port.Any(c => !char.IsAsciiDigit(c)))
        {
            throw new BrowseException("Invalid host port");
        }

        if (number == agentPort)
        {
            throw new BrowseException("Refusing to browse the agent listen port");
        }

        var matches = binds.Where(b => b.HostPort.Trim() == port).ToList();
        if (matches.Count == 0)
        {
            throw new BrowseException("Host port is not published on this container");
        }

        if (matches.Any(b => !LoopbackBinds.Contains(b.HostIp.Trim())))
        {
            throw new BrowseException("Refusing non-loopback published bind");
        }

        return $"http://127.0.0.1:{port}/";
    }

    /// <summary>True when <paramref name="href"/> stays on the session's port: a path, or 127.0.0.1/localhost on that port.</summary>
    public static bool IsAllowedUrl(string href, string hostPort)
    {
        var raw = href.Trim();
        if (!int.TryParse(hostPort, out var port) || raw.Length == 0)
        {
            return false;
        }

        if (raw.StartsWith('/') && !raw.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && uri.Host.ToLowerInvariant() is "127.0.0.1" or "localhost"
            && uri.Port == port;
    }

    /// <summary>Full and short ids name the same container: both become the first 12 hex characters.</summary>
    public static string NormalizeContainerId(string container)
    {
        var raw = container.Trim().ToLowerInvariant();
        return raw.Length >= 12 && raw.All(char.IsAsciiHexDigitLower) ? raw[..12] : raw;
    }

    public static bool SameContainer(string a, string b) => NormalizeContainerId(a) == NormalizeContainerId(b);

    /// <summary>The pane's device-stable id when it is a sane one; otherwise one for this connection.</summary>
    public static string NormalizeViewerId(string? raw)
    {
        var text = (raw ?? "").Trim();
        return ViewerId().IsMatch(text) ? text : Token(12);
    }

    /// <summary>
    /// One host browser per client, viewer device, container and port: two phones
    /// on the same agent do not share a page, and reopening on one resumes it.
    /// </summary>
    public static string SessionKey(string container, string hostPort, string client, string viewerId) =>
        $"{(client.Trim().Length > 0 ? client.Trim() : "anon")}|{(viewerId.Trim().Length > 0 ? viewerId.Trim() : "default")}|{NormalizeContainerId(container)}|{hostPort.Trim()}";

    /// <summary>A URL-safe random token of <paramref name="bytes"/> bytes.</summary>
    public static string Token(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex ViewerId();
}

/// <summary>A refusal or failure the pane shows as it is.</summary>
public sealed class BrowseException(string message, Exception? inner = null) : Exception(message, inner);
