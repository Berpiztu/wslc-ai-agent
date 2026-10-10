namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// The launch form's fields by name: what a check finding or a failed launch
/// points at, so the form shows the message in that field instead of under
/// everything. <see cref="General"/> is no field: the message goes above the
/// fields.
/// </summary>
public static class LaunchFields
{
    public const string General = "";
    public const string Image = "image";
    public const string Name = "name";
    public const string Publish = "publish";
    public const string Volumes = "volumes";
    public const string Env = "env";
    public const string Networks = "networks";
    public const string PublicNames = "publicNames";
    public const string Entrypoint = "entrypoint";
    public const string Command = "command";
    public const string Workdir = "workdir";
    public const string User = "user";
    public const string RestartPolicy = "restartPolicy";
    public const string StopTimeout = "stopTimeout";
    public const string HealthCmd = "healthCmd";
    public const string HealthInterval = "healthInterval";
    public const string HealthTimeout = "healthTimeout";
    public const string HealthRetries = "healthRetries";
    public const string HealthStartPeriod = "healthStartPeriod";
    public const string Memory = "memory";
    public const string Cpus = "cpus";
    public const string Labels = "labels";
    public const string Hostname = "hostname";
    public const string Domainname = "domainname";
    public const string Dns = "dns";
    public const string DnsSearch = "dnsSearch";
    public const string DnsOptions = "dnsOptions";
    public const string Tmpfs = "tmpfs";
    public const string ShmSize = "shmSize";
    public const string Ulimits = "ulimits";
    public const string StopSignal = "stopSignal";
}

/// <summary>One thing the check found, on one field of the form (<see cref="LaunchFields"/>).</summary>
public sealed record LaunchFinding(string Field, string Message);

/// <summary>
/// Answer of <c>POST /api/v1/containers/launch-check</c>: what would stop the
/// launch, and what looks wrong but may be right (a product listening where its
/// command does not say). Every message quotes the value it is about in full,
/// since the field that holds it may be cut short on a narrow screen.
/// </summary>
public sealed record LaunchCheck(IReadOnlyList<LaunchFinding> Errors, IReadOnlyList<LaunchFinding> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>Body of <c>POST /api/v1/containers/launch-check</c>.</summary>
/// <param name="Request">The form as it would be sent.</param>
/// <param name="Source">View &amp; edit: the container being recreated, whose own name and ports are not conflicts.</param>
public sealed record LaunchCheckRequest(ContainerLaunchRequest Request, string Source = "");
