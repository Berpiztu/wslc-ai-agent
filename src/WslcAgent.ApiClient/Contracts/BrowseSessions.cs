namespace WslcAgent.ApiClient.Contracts;

/// <summary>One live host browser: Edge on the agent's machine showing a container's published port.</summary>
/// <param name="Id">What a pane joins with (<c>sessionId</c> on the stream).</param>
/// <param name="ContainerId">The container's short id.</param>
/// <param name="HostPort">The published host port the browser shows.</param>
/// <param name="Url">The page it is on now.</param>
/// <param name="Client">Who opened it: <c>local</c>, or the remote address.</param>
/// <param name="OwnerViewerId">The device id of the pane that opened it.</param>
/// <param name="Viewers">Panes attached right now.</param>
/// <param name="Created">When the browser was launched.</param>
public sealed record BrowseSessionInfo(
    string Id,
    string ContainerId,
    string HostPort,
    string Url,
    string Client,
    string OwnerViewerId,
    int Viewers,
    DateTimeOffset Created);

/// <summary>Body of <c>GET /api/v1/containers/{container}/browse-sessions</c>.</summary>
public sealed record BrowseSessionList(IReadOnlyList<BrowseSessionInfo> Sessions);
