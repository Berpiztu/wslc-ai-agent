namespace WslcAgent.ApiClient.Contracts;

/// <summary>One row of <c>GET /api/v1/volumes</c>.</summary>
/// <param name="Name">Volume name (anonymous volumes have a 64-hex name).</param>
/// <param name="Driver"><c>guest</c> (inside the utility VM) or <c>vhd</c>.</param>
/// <param name="Mountpoint">Path as the CLI prints it; for <c>guest</c> volumes a path inside the utility VM, not on this host. Empty when unknown.</param>
/// <param name="Scope">Volume scope, normally <c>local</c>.</param>
/// <param name="Labels">Labels as the CLI prints them (<c>k=v,k2=v2</c>).</param>
/// <param name="InUse">Some container (any state) mounts the volume.</param>
/// <param name="Containers">How many containers, any state, mount the volume.</param>
/// <param name="Running">How many of those are running.</param>
/// <param name="DiskReadBytes">What the containers that mount it have read from disk, together (each container's whole figure from <c>stats</c>): the card's Read dial.</param>
/// <param name="DiskWriteBytes">What they have written, together: the card's Written dial.</param>
/// <param name="Uid">The agent's own number for this volume (the resource registry): what a resource card on the dashboard points at.</param>
public sealed record VolumeSummary(
    string Name,
    string Driver,
    string Mountpoint,
    string Scope,
    string Labels,
    bool InUse,
    int Containers,
    int Running,
    long DiskReadBytes,
    long DiskWriteBytes,
    int Uid = 0)
{
    /// <summary>A <c>guest</c> volume's mountpoint lives inside the utility VM and cannot be opened from this host.</summary>
    public bool MountpointIsInsideVm => Mountpoint.StartsWith("/var/lib/", StringComparison.Ordinal);
}

/// <summary>Body of <c>GET /api/v1/volumes</c>.</summary>
/// <param name="DiskReadBytes">What every container has read from disk: what a volume's Read dial is a share of.</param>
/// <param name="DiskWriteBytes">What every container has written: what a volume's Written dial is a share of.</param>
public sealed record VolumeListResponse(IReadOnlyList<VolumeSummary> Volumes, int Count, long DiskReadBytes, long DiskWriteBytes);

/// <summary>
/// Body of <c>POST /api/v1/volumes</c>. <paramref name="Size"/> (e.g. <c>8GB</c>) and
/// <paramref name="Fixed"/> apply to the <c>vhd</c> driver only. <paramref name="Label"/>
/// is <c>key=value</c>; <paramref name="Option"/> a driver option <c>key=value</c>.
/// </summary>
public sealed record CreateVolumeRequest(
    string Name,
    string Driver = "",
    string Size = "",
    bool Fixed = false,
    string Label = "",
    string Option = "");

/// <summary>Body of <c>GET /api/v1/volumes/{name}/inspect</c>: the raw <c>wslc volume inspect</c> JSON, indented.</summary>
public sealed record VolumeInspect(string Name, string Json);

/// <summary>One container that mounts a volume, and where.</summary>
/// <param name="Name">Container name.</param>
/// <param name="Id">Short container id.</param>
/// <param name="Image">Image the container runs.</param>
/// <param name="State">Container state (<c>running</c>, <c>exited</c>…).</param>
/// <param name="Destination">Where the volume is mounted inside the container.</param>
/// <param name="ReadWrite">False for a read-only mount.</param>
public sealed record VolumeUser(string Name, string Id, string Image, string State, string Destination, bool ReadWrite);

/// <summary>Body of <c>GET /api/v1/volumes/{name}/containers</c>.</summary>
public sealed record VolumeUsers(string Name, IReadOnlyList<VolumeUser> Containers);
