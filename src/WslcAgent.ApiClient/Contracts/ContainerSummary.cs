namespace WslcAgent.ApiClient.Contracts;

/// <summary>One row of <c>GET /api/v1/containers</c>: the list row merged with the container's stats.</summary>
/// <param name="Id">Short container id (12 hex characters).</param>
/// <param name="Name">Primary name, without the leading slash.</param>
/// <param name="Image">Image reference the container was created from.</param>
/// <param name="State">Lower-case state: <c>running</c>, <c>exited</c>, <c>created</c>, <c>paused</c>, … — and <c>recreating</c>, the agent's own, while a change to the container is being applied: the row stays in the list through the moment the container does not exist.</param>
/// <param name="Status">Human status as the CLI prints it, e.g. <c>Exited (255) 3 days ago</c>.</param>
/// <param name="Command">Entry command, without the surrounding quotes.</param>
/// <param name="CreatedAt">Creation time as the CLI prints it.</param>
/// <param name="RunningFor">Relative creation time, e.g. <c>3 weeks ago</c>.</param>
/// <param name="Ports">Port bindings as <c>host:port-&gt;container/proto</c>.</param>
/// <param name="Networks">Comma-separated network names.</param>
/// <param name="Size">Writable layer size as printed.</param>
/// <param name="HealthStatus">Health check status, empty when none.</param>
/// <param name="Platform"><c>os/architecture</c>, e.g. <c>linux/amd64</c>.</param>
/// <param name="CpuPercent">CPU usage as printed by <c>wslc stats</c> (<c>0.40%</c>), empty when unknown.</param>
/// <param name="MemUsage">Memory usage as printed (<c>885.1MiB / 7.6GiB</c>), empty when unknown.</param>
/// <param name="MemPercent">Memory usage percent as printed, empty when unknown.</param>
/// <param name="DiskIo">Block I/O as printed (<c>1.2MB / 0B</c>), empty when unknown.</param>
/// <param name="RestartPolicy">Dashboard restart policy (<c>always</c>, <c>unless-stopped</c>), empty when none.</param>
/// <param name="Publications">The public names this container's ports are published on, from the agent's store; null when it has none.</param>
/// <param name="NetIo">Network I/O as printed (<c>3.1kB / 2.2kB</c>, received / sent), empty when unknown.</param>
/// <param name="DiskIoBytes">Block I/O read and written together, in bytes: the card's Disk dial and its share of every container's.</param>
/// <param name="NetIoBytes">Network I/O received and sent together, in bytes: the card's Network dial and its share of every container's.</param>
/// <param name="Uid">The agent's own number for this container (the resource registry, <c>resources.json</c>): no rename and no recreate changes it, so the dashboard points at it; 0 for a row the registry does not hold, such as a Files helper.</param>
/// <param name="UpdatingTo">The image this container's image update is pulling, while the pull lasts (<c>/images/pulls</c> has its progress); null otherwise.</param>
/// <param name="PatchingTo">The patch this container is to be moved to, while Zaindari's patch is built and moves it (<c>/image-security/patch</c>); null otherwise.</param>
/// <param name="PatchJob">That patch's build job (<c>/images/build/{job}</c>), its console and its cancel; null otherwise.</param>
/// <param name="Project">The project it belongs to, when it was made from a Compose file (its <c>ai.berpiztu.wslc.project</c> label); empty for a container on its own.</param>
/// <param name="Service">Its service in that project's Compose file; empty for a container on its own.</param>
/// <param name="StartError">Why it did not start, the last time it was asked to, as <c>wslc</c> said it; empty once it has started, and for one never asked. Its logs end with the same.</param>
/// <param name="MissingServices">The services of its project that no container stands for, by the file kept with the project: the project is incomplete (a container was removed around the agent, or a save stopped half way), and a save makes them again. Null when it is complete, or the container is of no project.</param>
/// <param name="HoldsNameOf">The service of another project whose container is gone and whose name this one holds (<c>service backend of project shop</c>): a container made around the agent under it, so that project cannot make its own again while this one has the name. Empty otherwise.</param>
/// <param name="ImageRefused">Its image is one the agent makes no new container from (built here, a vulnerability moderate or worse, Settings → Security set to Block): the image's own mark, read by its name (<see cref="ImageSummary.ImageRefused"/>).</param>
/// <param name="ImageWorst">The worst severity of its image's vulnerabilities moderate or worse, as Zaindari last found it in the image: <c>moderate</c>, <c>high</c> or <c>critical</c>; empty for an image with none, or not checked.</param>
/// <param name="ImageCheckedAt">When Zaindari last checked its image (<c>yyyy-MM-dd HH:mm</c>, the agent's time); empty with no mark.</param>
public sealed record ContainerSummary(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    string Command,
    string CreatedAt,
    string RunningFor,
    IReadOnlyList<string> Ports,
    string Networks,
    string Size,
    string HealthStatus,
    string Platform,
    string CpuPercent = "",
    string MemUsage = "",
    string MemPercent = "",
    string DiskIo = "",
    string RestartPolicy = "",
    IReadOnlyList<Publication>? Publications = null,
    string NetIo = "",
    long DiskIoBytes = 0,
    long NetIoBytes = 0,
    int Uid = 0,
    string? UpdatingTo = null,
    string Project = "",
    string Service = "",
    string StartError = "",
    IReadOnlyList<string>? MissingServices = null,
    string HoldsNameOf = "",
    bool ImageRefused = false,
    string ImageWorst = "",
    string ImageCheckedAt = "",
    string? PatchingTo = null,
    string? PatchJob = null)
{
    /// <summary>The agent's own state while a change to the container is being applied (a recreate).</summary>
    public const string Recreating = "recreating";

    public bool IsRunning => State == "running";

    /// <summary>The code what it runs ended with, read from the status as the CLI prints it (<c>Exited (1) 2 minutes ago</c>); null while it runs, and for one that never ran.</summary>
    public int? ExitCode
    {
        get
        {
            const string before = "Exited (";
            if (State != "exited" || !Status.StartsWith(before, StringComparison.Ordinal))
            {
                return null;
            }

            var end = Status.IndexOf(')', before.Length);
            return end > 0 && int.TryParse(Status.AsSpan(before.Length, end - before.Length), out var code) ? code : null;
        }
    }

    /// <summary>
    /// It is down and not because someone asked: it could not start
    /// (<see cref="StartError"/>), or what it runs ended with an error. A
    /// code of 0 is an end, and 137 and 143 are what a stop leaves (the kill
    /// and the term signals, 128 and the signal's number): neither is a
    /// failure.
    /// </summary>
    public bool HasFailed => !IsRunning && (StartError.Length > 0 || ExitCode is not (null or 0 or 137 or 143));

    /// <summary>Why it is down, in words, when it <see cref="HasFailed"/>; empty otherwise.</summary>
    public string Failure =>
        !HasFailed ? ""
        : StartError.Length > 0 ? $"Could not start: {StartError}"
        : $"Exited with code {ExitCode}: its logs say why";

    /// <summary>Its image has known vulnerabilities moderate or worse (<see cref="ImageWorst"/>).</summary>
    public bool HasImageWarning => ImageWorst.Length > 0;

    /// <summary>Something about it to mend that stops nothing: an image with known vulnerabilities, or another project's container's name it holds (<see cref="HoldsNameOf"/>). Drawn in the yellow, for whoever sees it to look why.</summary>
    public bool IsWarned => HasImageWarning || HoldsNameOf.Length > 0;

    /// <summary>A change to it is being applied: for these seconds it may not exist at all, and no verb has a container to act on.</summary>
    public bool IsRecreating => State == Recreating;

    /// <summary>Its image update is pulling the new image: the recreate that follows will replace it.</summary>
    public bool IsUpdating => UpdatingTo is not null;

    /// <summary>The publications, an empty list when there are none: the field is optional on the wire.</summary>
    public IReadOnlyList<Publication> Published => Publications ?? [];
}

/// <summary>Usage across all listed containers, for the page header.</summary>
/// <param name="CpuUsedPercent">Sum of the containers' CPU percentages.</param>
/// <param name="CpuCount">Logical CPUs on the agent machine; the total is <c>CpuCount * 100</c> percent.</param>
/// <param name="MemoryUsedBytes">Sum of the containers' used memory.</param>
public sealed record ContainerAggregate(double CpuUsedPercent, int CpuCount, long MemoryUsedBytes);

/// <summary>Body of <c>GET /api/v1/containers</c>.</summary>
public sealed record ContainerListResponse(IReadOnlyList<ContainerSummary> Containers, ContainerAggregate Aggregate);
