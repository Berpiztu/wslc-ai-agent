namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Settings → Publishing: how this agent's machine publishes a container port on a
/// public name. The proxy container reads the map file the agent writes, on the
/// network where container names resolve; the VPS sends every name under the
/// domain down the tunnel to that proxy.
/// </summary>
/// <param name="Domain">The domain the public names end in, e.g. <c>example.com</c>; a wildcard certificate and DNS entry cover every single label under it.</param>
/// <param name="NameSuffix">What the suggested name adds to the container's, e.g. <c>-home</c>, so containers of several machines under one domain do not collide.</param>
/// <param name="ProxyContainer">The nginx container that reads the map, e.g. <c>wslc-published</c>; restarted after every change.</param>
/// <param name="Network">The user-defined network the proxy and the published containers share, e.g. <c>published</c>; a container is published only while it and the proxy are both on it, and the launch form offers to add it.</param>
/// <param name="MapFile">Where the agent writes the map the proxy mounts, e.g. <c>C:\wslc\published.conf</c>.</param>
public sealed record PublishingSettings(string Domain, string NameSuffix, string ProxyContainer, string Network, string MapFile);

/// <summary>One published port: a public name that reaches one port of one container.</summary>
/// <param name="Container">The container's name.</param>
/// <param name="ContainerPort">The port inside the container (not a published host port: the proxy reaches the container by name).</param>
/// <param name="Hostname">The full public name, e.g. <c>webui-home.example.com</c>.</param>
public sealed record Publication(string Container, int ContainerPort, string Hostname)
{
    /// <summary>What a browser opens: TLS ends at the VPS, so it is always https.</summary>
    public string Url => $"https://{Hostname}/";
}

/// <summary>Body of <c>POST /api/v1/publications</c>.</summary>
public sealed record PublishRequest(string Container, int ContainerPort, string Hostname);

/// <summary>Body of <c>GET /api/v1/publications</c>.</summary>
public sealed record PublicationList(IReadOnlyList<Publication> Publications);

/// <summary>Answer of <c>POST /api/v1/publishing/setup</c>: what the first-use setup did, and what it found there already.</summary>
/// <param name="NetworkCreated">The network of the settings was missing and was created.</param>
/// <param name="MapWritten">The map file was missing and was written (empty of names, or with the store's).</param>
/// <param name="ProxyCreated">The nginx container was missing and was run.</param>
/// <param name="Notes">One line per step, for the person.</param>
public sealed record PublishingSetupResult(bool NetworkCreated, bool MapWritten, bool ProxyCreated, IReadOnlyList<string> Notes);
