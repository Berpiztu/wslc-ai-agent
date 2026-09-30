using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Mcp;

/// <summary>The Home page's overview and metrics. Implemented by the server.</summary>
public interface IHomeService
{
    /// <summary>Containers running and total, images and their size, networks, volumes, aggregate CPU and memory, the tool version and the selected session.</summary>
    Task<HomeOverview> OverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>One sample of the containers' aggregate CPU and memory.</summary>
    Task<HomeRuntime> RuntimeAsync(CancellationToken cancellationToken = default);

    /// <summary>One sample of the containers' aggregate disk and network I/O.</summary>
    Task<HomeIo> IoAsync(CancellationToken cancellationToken = default);

    /// <summary>What is used of the Windows drive the sessions' VHDX files are on, and its size.</summary>
    HomeDisk Disk();
}
