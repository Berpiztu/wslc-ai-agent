using System.Net;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Publishing;

namespace WslcAgent.Server.Tests;

/// <summary>The wslc-published plugin as the tests script it: what the agent asked it for, and the sites it holds.</summary>
public sealed class FakePublishedPlugin : IPublishedPlugin
{
    private readonly Dictionary<string, Publication> _sites = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public Task PutSiteAsync(Publication publication, CancellationToken cancellationToken = default)
    {
        Calls.Add($"put {publication.Hostname} {publication.Container}:{publication.ContainerPort}");
        _sites[publication.Hostname] = publication;
        return Task.CompletedTask;
    }

    public Task RemoveSiteAsync(string hostname, CancellationToken cancellationToken = default)
    {
        Calls.Add($"remove {hostname}");
        _sites.Remove(hostname);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PluginSite>> SitesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PluginSite>>(_sites.Values.Select(Site).ToList());

    public Task<PluginSite> CheckAsync(string hostname, CancellationToken cancellationToken = default)
    {
        Calls.Add($"check {hostname}");
        return Task.FromResult(Site(_sites[hostname]));
    }

    public Task<PluginSite> ForceLoginAsync(string hostname, bool force, CancellationToken cancellationToken = default)
    {
        Calls.Add($"force {hostname} {force}");
        return Task.FromResult(Site(_sites[hostname]) with { ForceLogin = force });
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        Calls.Add($"pass {request.Method} {request.RequestUri}");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<h1>Published names</h1>", System.Text.Encoding.UTF8, "text/html") });
    }

    private static PluginSite Site(Publication publication) =>
        new(publication.Hostname, publication.Container, publication.ContainerPort, "login", "Opens without asking for anything (HTTP 200).", "ABCD-EFGH-JKLM", []);
}
