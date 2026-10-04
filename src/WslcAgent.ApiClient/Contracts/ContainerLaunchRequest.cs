namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// The launch fields: what the Run, Create and View &amp; edit
/// forms send, and what <c>wslc container run|create</c> is built from.
/// Empty strings and empty lists mean "not given".
/// </summary>
public sealed record ContainerLaunchRequest
{
    /// <summary>Image reference (<c>repo[:tag]</c> or id). Required.</summary>
    public required string Image { get; init; }

    public string Name { get; init; } = "";

    /// <summary>Command after the image; empty runs the image ENTRYPOINT/CMD. Split like a shell.</summary>
    public string Command { get; init; } = "";

    public string Entrypoint { get; init; } = "";

    /// <summary>Memory limit as the CLI takes it, e.g. <c>512m</c>.</summary>
    public string Memory { get; init; } = "";

    /// <summary>CPU limit, e.g. <c>1.5</c>.</summary>
    public string Cpus { get; init; } = "";

    /// <summary>Port publications, <c>[host:]hostPort:containerPort</c>, one per <c>--publish</c>.</summary>
    public IReadOnlyList<string> Publish { get; init; } = [];

    /// <summary>Mounts, <c>source:target[:ro]</c> (a host path or a volume name), one per <c>--volume</c>.</summary>
    public IReadOnlyList<string> Volumes { get; init; } = [];

    /// <summary>Working directory inside the container. A host path here is remapped to the first volume's target.</summary>
    public string Workdir { get; init; } = "";

    /// <summary>Environment, <c>KEY=value</c>, one per <c>--env</c>.</summary>
    public IReadOnlyList<string> Env { get; init; } = [];

    /// <summary>Primary network; empty is the default bridge.</summary>
    public string Network { get; init; } = "";

    /// <summary>Static IPv4 on the primary network; honoured on user-defined networks only.</summary>
    public string Ip { get; init; } = "";

    /// <summary>Aliases on the primary network, one per <c>--network-alias</c>.</summary>
    public IReadOnlyList<string> NetworkAliases { get; init; } = [];

    /// <summary>Extra networks attached after the container exists: <c>name</c> or <c>name 172.19.0.2</c>.</summary>
    public IReadOnlyList<string> ConnectNetworks { get; init; } = [];

    public string User { get; init; } = "";

    /// <summary>Agent-owned restart policy: <c>no</c>, <c>unless-stopped</c> or <c>always</c>. Not a CLI flag.</summary>
    public string RestartPolicy { get; init; } = "no";

    /// <summary>
    /// Agent-owned public names, one row per port to publish, <c>containerPort:name</c>
    /// (<c>8080:open-webui</c>); the suffix and the domain come from Settings →
    /// Publishing. Saving the form publishes and unpublishes the difference. Not a CLI flag.
    /// </summary>
    public IReadOnlyList<string> PublicNames { get; init; } = [];

    /// <summary>Seconds before a stop becomes a kill; <c>0</c> immediate, <c>-1</c> never.</summary>
    public string StopTimeout { get; init; } = "";

    public string HealthCmd { get; init; } = "";

    public string HealthInterval { get; init; } = "";

    public string HealthTimeout { get; init; } = "";

    public string HealthRetries { get; init; } = "";

    public string HealthStartPeriod { get; init; } = "";

    /// <summary>Emit <c>--no-healthcheck</c> and drop every health field.</summary>
    public bool NoHealthcheck { get; init; }

    /// <summary>Start the container (run detached) rather than only create it.</summary>
    public bool Start { get; init; } = true;
}

/// <summary>Answer of create, run and recreate: the new container's id.</summary>
public sealed record ContainerCreated(string Id, IReadOnlyList<string> Notes);

/// <summary>Agent-owned restart policy of one container.</summary>
/// <param name="Policy"><c>no</c>, <c>unless-stopped</c> or <c>always</c>.</param>
/// <param name="Desired"><c>running</c> or <c>stopped</c>: what the user last asked for.</param>
/// <param name="Enrolled">False when the policy is <c>no</c> (nothing stored).</param>
public sealed record RestartPolicyInfo(string Policy, string Desired, bool Enrolled)
{
    public const string No = "no";
    public const string UnlessStopped = "unless-stopped";
    public const string Always = "always";
    public const string Running = "running";
    public const string Stopped = "stopped";

    public static readonly RestartPolicyInfo None = new(No, Stopped, false);

    public static bool IsKnown(string policy) => policy is No or UnlessStopped or Always;
}

/// <summary>Body of <c>POST /api/v1/containers/{id}/update-image</c>.</summary>
/// <param name="Image">The new image with its version: <c>ghcr.io/owner/app:1.5</c>.</param>
public sealed record UpdateContainerImageRequest(string Image);

/// <summary>Body of <c>PUT /api/v1/containers/{id}/restart-policy</c>.</summary>
public sealed record SetRestartPolicyRequest(string Policy);

/// <summary>One mount of a container, for the details view.</summary>
public sealed record MountInfo(string Type, string Source, string Destination, string Mode);

/// <summary>Body of <c>GET /api/v1/containers/{id}/details</c>: header, mounts, the form the container was launched with, its policy, its public names, and the raw inspect JSON.</summary>
/// <param name="Uid">The agent's own number for this container, as on its list row: what a resource card on the dashboard points at.</param>
public sealed record ContainerDetails(
    string Id,
    string Name,
    string Image,
    string State,
    bool IsRunning,
    IReadOnlyList<string> Ports,
    string Created,
    IReadOnlyList<MountInfo> Mounts,
    ContainerLaunchRequest Form,
    RestartPolicyInfo RestartPolicy,
    string Inspect,
    IReadOnlyList<Publication> Publications,
    int Uid = 0);

/// <summary>Body of <c>GET /api/v1/containers/{id}/logs</c>.</summary>
public sealed record ContainerLogs(string Text);

/// <summary>Body of <c>GET</c> and <c>PUT /api/v1/containers/{id}/logs/cleared</c>: the stamp of the last line cleared in a viewer (the container's clock), or null when the history shows whole.</summary>
public sealed record ContainerLogsCleared(DateTimeOffset? ClearedAt);

/// <summary>Body of <c>POST /api/v1/containers/launch-form</c>: an inspect JSON, or an exported launch request, as text.</summary>
public sealed record LaunchFormSource(string Json);

/// <summary>Body of <c>GET /api/v1/host/folders</c>: one level of the agent machine's folders.</summary>
public sealed record HostFolderListing(string Path, string Parent, IReadOnlyList<string> Folders);

/// <summary>Body of <c>POST /api/v1/host/folders</c>.</summary>
public sealed record CreateHostFolderRequest(string Parent, string Name);

/// <summary>
/// A Run or a Create the agent carries out after the dialog closed: the image
/// is pulled first when it is not local, then the container is run, or only
/// created when the request's <c>Start</c> is false. Rows of
/// <c>GET /api/v1/containers/launches</c>.
/// </summary>
/// <param name="Name">The container name asked for, or <c>run-</c> and a short id.</param>
/// <param name="Phase"><c>pull</c>, <c>run</c>, <c>done</c>, <c>error</c> or <c>cancelled</c>.</param>
/// <param name="Status">The line to show: the pull's progress, <c>Starting container…</c> or <c>Creating container…</c>, or why it ended.</param>
/// <param name="Pct">The pull's percentage; 100 once the run starts.</param>
/// <param name="ContainerId">The container created, once done.</param>
/// <param name="Notes">What the run had to adjust (a static ip dropped), once done.</param>
/// <param name="Fields">Once failed: the form's fields the failure is about (<see cref="LaunchFields"/>), each with the CLI's reason.</param>
public sealed record ContainerLaunch(
    string Id,
    string Image,
    string Name,
    string Phase,
    string Status,
    int Pct,
    string Error,
    string ContainerId,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string>? Fields = null)
{
    private static readonly IReadOnlyDictionary<string, string> NoFields = new Dictionary<string, string>();

    public bool Active => Phase is "pull" or "run";

    /// <summary><see cref="Fields"/>, never null: an older agent sends none.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors => Fields ?? NoFields;
}
