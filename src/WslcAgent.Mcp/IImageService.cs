using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>Image operations the MCP tools need from the host. Implemented by the server.</summary>
public interface IImageService
{
    /// <summary>Images on the selected session, with usage and totals.</summary>
    Task<ImageListResponse> ListAsync(CancellationToken cancellationToken = default);

    /// <summary><c>wslc image inspect REFERENCE</c>: the raw JSON, indented.</summary>
    Task<ImageInspect> InspectAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>What a local image sets for every container made from it, as launch fields; an image that is not on the machine says so and nothing else.</summary>
    Task<ImageDefaults> DefaultsAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary><c>wslc image pull [--all-tags] REFERENCE</c>. Slow: minutes for a large image.</summary>
    Task PullAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default);

    /// <summary><c>wslc image push [--all-tags] REFERENCE</c>, to the registry the reference names. Slow for large images.</summary>
    Task PushAsync(string reference, bool allTags = false, CancellationToken cancellationToken = default);

    /// <summary><c>wslc image save --output PATH REFERENCE</c> on the agent's machine; a bare file name goes to the user's Downloads.</summary>
    Task<SavedImage> SaveAsync(string reference, string output, CancellationToken cancellationToken = default);

    /// <summary><c>wslc image tag SOURCE TARGET</c>.</summary>
    Task TagAsync(string source, string target, CancellationToken cancellationToken = default);

    /// <summary><c>wslc image remove [--force] REFERENCE</c>. Destructive: callers confirm with the user first.</summary>
    Task RemoveAsync(string reference, bool force, CancellationToken cancellationToken = default);

    /// <summary><c>wslc image prune</c>: dangling images only (no name, no tag), or with <paramref name="all"/> every image no container uses (<c>-a</c>). Destructive.</summary>
    Task<CommandOutput> PruneAsync(bool all = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// The pulls the agent has running or finished, the same jobs its UI shows.
    /// A pull is slow enough that a client may give up waiting; this is how it
    /// asks again instead of starting the download over.
    /// </summary>
    Task<IReadOnlyList<ImagePullState>> PullsAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a pull as a job and answers its first state, for a caller that will poll rather than wait.</summary>
    Task<ImagePullState> StartPullAsync(string reference, CancellationToken cancellationToken = default);
}
