using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>The WSLC host as the System page shows it. Implemented by the server.</summary>
public interface ISystemService
{
    /// <summary>Tool version, runtime info, sessions, the session VHDX files and the largest images.</summary>
    Task<SystemOverview> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Prunes <paramref name="target"/> (<c>images</c> with <c>-a</c>, <c>volumes</c>
    /// or <c>networks</c>) and measures what it gave back. Destructive. Throws
    /// <see cref="ArgumentException"/> for any other target.
    /// </summary>
    Task<CleanupResult> CleanupAsync(string target, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the selected session's store holds and how much of it no image and
    /// no container uses. Reads only, and only while that session runs:
    /// <see cref="InvalidOperationException"/> when it does not, since reading
    /// it would start it.
    /// </summary>
    Task<StoreReclaimable> ReclaimableAsync(CancellationToken cancellationToken = default);
}
