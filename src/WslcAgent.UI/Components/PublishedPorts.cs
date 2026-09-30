using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// The "open with browser" rules over the <c>hostPort-&gt;containerPort</c>
/// strings a container publishes: which port opens by default and what URL
/// reaches it on the agent's host.
/// </summary>
public static class PublishedPorts
{
    /// <summary>Web ports first, in this order; any other host port after, lowest first.</summary>
    private static readonly int[] Preferred = [80, 443, 8000, 8080, 8443, 3000, 5000, 5173, 4200, 8888, 9090];

    /// <summary>A published port pair; <see cref="Url"/> targets the agent's host.</summary>
    public sealed record Published(int HostPort, int ContainerPort)
    {
        public string Label => $"{HostPort} → {ContainerPort}";

        /// <summary>https only for the TLS ports (443, 8443 on the host, 443 in the container).</summary>
        public string Url(string host)
        {
            var scheme = HostPort is 443 or 8443 || ContainerPort == 443 ? "https" : "http";
            return $"{scheme}://{host}:{HostPort}/";
        }
    }

    public static IReadOnlyList<Published> Parse(IEnumerable<string> ports)
    {
        var parsed = new List<Published>();
        foreach (var text in ports)
        {
            var parts = text.Split("->", 2);
            if (int.TryParse(parts[0].Trim(), out var host) && (parts.Length == 1 || int.TryParse(parts[1].Trim(), out _)))
            {
                parsed.Add(new Published(host, parts.Length > 1 ? int.Parse(parts[1].Trim()) : host));
            }
        }

        return parsed
            .OrderBy(p => Array.IndexOf(Preferred, p.HostPort) is var i && i >= 0 ? i : Preferred.Length)
            .ThenBy(p => p.HostPort)
            .ToList();
    }

    /// <summary>The https name a port is published on (Publish), if any: it opens from anywhere.</summary>
    public static string? PublicUrl(Published port, IReadOnlyList<Publication> publications) =>
        publications.FirstOrDefault(p => p.ContainerPort == port.ContainerPort)?.Url;

    /// <summary>The host a browser reaches the agent at; <c>0.0.0.0</c> and <c>::</c> read as localhost.</summary>
    public static string HostOf(Uri? agent)
    {
        var host = agent?.Host ?? "localhost";
        return host is "0.0.0.0" or "::" or "[::]" ? "localhost" : host;
    }
}
