using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>Container operations the MCP tools need from the host. Implemented by the server.</summary>
public interface IContainerService
{
    /// <summary>Containers on the selected session (running only unless <paramref name="all"/>) with their stats and the aggregate; the agent's temporary Files helper containers only with <paramref name="helpers"/>.</summary>
    Task<ContainerListResponse> ListAsync(bool all, bool helpers = false, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container start</c>. <paramref name="container"/> is a name or id.</summary>
    Task StartAsync(string container, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container stop</c>.</summary>
    Task StopAsync(string container, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container restart</c>.</summary>
    Task RestartAsync(string container, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container kill</c>: no grace period. Destructive for the process, not the container.</summary>
    Task KillAsync(string container, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container rm [--force]</c>; without <paramref name="force"/> a running container is refused. Destructive: callers confirm with the user first.</summary>
    Task RemoveAsync(string container, bool force = false, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container create OPTIONS image [command]</c>, then the extra networks and the restart policy.</summary>
    Task<ContainerCreated> CreateAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container run --detach OPTIONS image [command]</c> (pulls a missing image first: minutes), then the extra networks and the restart policy.</summary>
    Task<ContainerCreated> RunAsync(ContainerLaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Stops the container, rehearses the request under a throwaway name (a failure there starts the container again and changes nothing), then removes it and creates it again from the request; if that launch fails the previous container is restored. Destructive: callers confirm first.</summary>
    Task<ContainerCreated> RecreateAsync(string container, ContainerLaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Header, mounts, the launch form the container was created with, its restart policy and the raw inspect JSON.</summary>
    Task<ContainerDetails> DetailsAsync(string container, CancellationToken cancellationToken = default);

    /// <summary><c>wslc container logs [--tail N] [--timestamps] [--since T]</c>: stderr (lifecycle) first, then stdout. <paramref name="since"/> (RFC 3339) keeps only the lines from that moment on.</summary>
    Task<ContainerLogs> LogsAsync(string container, int tail, bool timestamps, CancellationToken cancellationToken = default, string since = "");

    /// <summary>One <c>wslc container stats</c> sample of the container, as numbers.</summary>
    Task<ContainerStats> StatsAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>
    /// One non-interactive <c>wslc exec</c>: no TTY, no stdin. The command's own
    /// failure comes back as a non-zero exit code with its stderr, not as an
    /// exception. Destructive: a shell inside a container can change anything.
    /// </summary>
    Task<ContainerExecResult> ExecAsync(string container, string command, CancellationToken cancellationToken = default);

    /// <summary>The raw inspect JSON, indented, with the name the file is saved under.</summary>
    Task<(string Name, string Json)> InspectJsonAsync(string container, CancellationToken cancellationToken = default);

    /// <summary>The launch form read from an inspect JSON or an exported launch request; <see cref="ArgumentException"/> when it is neither.</summary>
    ContainerLaunchRequest LaunchFormFromJson(string json);

    RestartPolicyInfo GetRestartPolicy(string container);

    /// <summary>Enrols or unenrols the container; <c>no</c> unenrols.</summary>
    Task<RestartPolicyInfo> SetRestartPolicyAsync(string container, string policy, CancellationToken cancellationToken = default);
}
