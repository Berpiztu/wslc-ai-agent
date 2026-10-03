namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// Settings → Publishing: how this agent's machine publishes a container port on a
/// public name. The proxy is the wslc-published plugin (plugins/wslc-published):
/// nginx and its own login in one container, on the network where container names
/// resolve; the VPS sends every name under the domain down the tunnel to it.
/// </summary>
/// <param name="Domain">The domain the public names end in, e.g. <c>example.com</c>; a wildcard certificate and DNS entry cover every single label under it.</param>
/// <param name="NameSuffix">What the suggested name adds to the container's, e.g. <c>-home</c>, so containers of several machines under one domain do not collide.</param>
/// <param name="ProxyContainer">The proxy container, e.g. <c>wslc-published</c>.</param>
/// <param name="Network">The user-defined network the proxy and the published containers share, e.g. <c>published</c>; a container is published only while it and the proxy are both on it, and the launch form offers to add it.</param>
/// <param name="DataFolder">The proxy's data folder, mounted at <c>/data</c>, e.g. <c>C:\wslc\published</c>: its sites, their users, its key and its admin token, which the agent reads to call it.</param>
public sealed record PublishingSettings(string Domain, string NameSuffix, string ProxyContainer, string Network, string DataFolder);

/// <summary>One published port: a public name that reaches one port of one container.</summary>
/// <param name="Container">The container's name.</param>
/// <param name="ContainerPort">The port inside the container (not a published host port: the proxy reaches the container by name).</param>
/// <param name="Hostname">The full public name, e.g. <c>webui-home.example.com</c>.</param>
public sealed record Publication(string Container, int ContainerPort, string Hostname)
{
    /// <summary>
    /// What a browser opens: TLS ends at the VPS, so it is https. A name under
    /// <c>localhost</c> is this PC's own, for trying publishing out with no VPS: a
    /// browser takes every <c>*.localhost</c> to this machine, where the proxy listens
    /// on <see cref="LocalPort"/>, over plain http.
    /// </summary>
    public string Url => Hostname.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        ? $"http://{Hostname}:{LocalPort}/"
        : $"https://{Hostname}/";

    /// <summary>The port the proxy publishes on this PC's loopback, where the VPS's tunnel arrives.</summary>
    public const int LocalPort = 8081;

    /// <summary>
    /// Who the proxy lets in, as it says: <c>own</c> (the application asks for a login of
    /// its own) or <c>login</c> (the proxy's login page, for the name's own users; while it
    /// has no administrator, the page creates one with the name's claim code). Null when
    /// the proxy did not answer, and in the store's own file.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Access { get; init; }

    /// <summary>How many users the name has in the proxy's login; who they are is in the proxy's own page.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? UserCount { get; init; }

    /// <summary>The proxy's login stands in front even though the application has its own: two logins, the proxy's first.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? ForceLogin { get; init; }

    /// <summary>What the proxy's last check found the application answers with no session, in words.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessNote { get; init; }
}

/// <summary>Body of <c>PUT /api/v1/publications/{hostname}/force-login</c>.</summary>
/// <param name="Force">The proxy's login in front of the name even when its application has its own.</param>
public sealed record ForceLoginRequest(bool Force);

/// <summary>Body of <c>POST /api/v1/publications</c>.</summary>
public sealed record PublishRequest(string Container, int ContainerPort, string Hostname);

/// <summary>Body of <c>GET /api/v1/publications</c>.</summary>
public sealed record PublicationList(IReadOnlyList<Publication> Publications);

/// <summary>Answer of <c>POST /api/v1/publishing/setup</c>: what the first-use setup did, and what it found there already.</summary>
/// <param name="NetworkCreated">The network of the settings was missing and was created.</param>
/// <param name="DataFolderCreated">The proxy's data folder was missing and was made.</param>
/// <param name="ProxyCreated">The proxy container was missing, or plain nginx, and was run.</param>
/// <param name="Notes">One line per step, for the person.</param>
public sealed record PublishingSetupResult(bool NetworkCreated, bool DataFolderCreated, bool ProxyCreated, IReadOnlyList<string> Notes);
