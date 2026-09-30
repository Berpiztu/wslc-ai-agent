using System.Text;
using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// The proxy's configuration, written from the publications and read back from
/// a copy written by hand. It is an nginx configuration
/// with the map generated: one line per published port, the destination being
/// the container's name and internal port on the shared network.
/// </summary>
public static partial class PublishedMap
{
    private const string Head = """
        # The published names of this machine, read by the proxy container.
        # Written by WSLC AI Agent (Settings -> Publishing): a hand edit lasts until
        # the next Publish or Unpublish, which writes the whole file again.
        #
        # The destinations are container names on the shared user-defined network,
        # where the embedded DNS resolves them, and the port is the container's own
        # internal one: a published container needs no published port on the host.

        map $host $wslc_upstream {
            hostnames;
            default                      "";

        """;

    private const string Tail = """
        }

        server {
            listen 80;
            server_name ~^.+$;

            # proxy_pass with a variable resolves the name at request time, and for
            # that nginx needs a resolver: 127.0.0.11 is the embedded DNS of a
            # user-defined network. Without it every request is a 502.
            resolver 127.0.0.11 valid=30s ipv6=off;

            # A name nobody published is not a mystery, it is a 404.
            if ($wslc_upstream = "") {
                return 404;
            }

            location / {
                proxy_pass $wslc_upstream;
                proxy_http_version 1.1;

                proxy_set_header Host $host;
                proxy_set_header X-Real-IP $remote_addr;
                proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                proxy_set_header X-Forwarded-Proto https;

                # Terminals and chats speak WebSocket.
                proxy_set_header Upgrade $http_upgrade;
                proxy_set_header Connection "upgrade";
                proxy_read_timeout 3600s;
                proxy_send_timeout 3600s;

                client_max_body_size 64m;
            }
        }

        """;

    public static string Render(IEnumerable<Publication> publications)
    {
        var text = new StringBuilder(Head);
        foreach (var publication in publications.OrderBy(p => p.Hostname, StringComparer.Ordinal))
        {
            text.Append("    ").Append(publication.Hostname.PadRight(28)).Append(" http://")
                .Append(publication.Container).Append(':').Append(publication.ContainerPort).Append(";\n");
        }

        return text.Append(Tail).ToString().Replace("\r\n", "\n");
    }

    /// <summary>
    /// The publications a map written by hand holds, so the first Publish keeps
    /// what was there instead of writing over it: every <c>name http://container:port;</c>
    /// line of the map block.
    /// </summary>
    public static IReadOnlyList<Publication> Parse(string text) =>
        MapLine().Matches(text)
            .Select(m => new Publication(m.Groups["container"].Value, int.Parse(m.Groups["port"].Value), m.Groups["host"].Value.ToLowerInvariant()))
            .ToList();

    [GeneratedRegex(@"^\s*(?<host>[a-z0-9-]+(?:\.[a-z0-9-]+)+)\s+http://(?<container>[A-Za-z0-9][A-Za-z0-9_.-]*):(?<port>\d{1,5});", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex MapLine();
}
