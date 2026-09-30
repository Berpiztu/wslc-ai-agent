using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Sources;
using WslcAgent.ApiClient;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What the dashboard's alarms watch: on an object that reads a container, the
/// container's own CPU or memory while it runs; on any other the host's — the
/// CPU used of all there is, the memory used of the session's, the drive the
/// sessions' VHDX files are on. Worked out here alone, from the reads the
/// objects already follow, for the dashboard and the status bar alike.
/// </summary>
public sealed class DashboardMeasures : IMeasures, IDisposable
{
    public const string Cpu = "cpu";

    public const string Memory = "memory";

    public const string Disk = "disk";

    /// <summary>A measure as the status bar names it, short enough for its narrow cells: CPU, Mem, Disk.</summary>
    public static string Short(string measure) => measure switch
    {
        Cpu => "CPU",
        Memory => "Mem",
        Disk => "Disk",
        _ => measure,
    };

    private readonly HostRuntimeRead _runtime;
    private readonly HostDiskRead _disk;
    private readonly ContainerRows _containers;

    public DashboardMeasures(HostRuntimeRead runtime, HostDiskRead disk, ContainerRows containers)
    {
        _runtime = runtime;
        _disk = disk;
        _containers = containers;
        _runtime.Changed += OnRead;
        _disk.Changed += OnRead;
        _containers.Changed += OnRead;
    }

    public event Action? Changed;

    public double? Of(ObjectInstance o, string measure)
    {
        if (RegistrySource.Uid(o.Source) is { } uid)
        {
            return _containers.Find(uid) is { IsRunning: true } container && measure is Cpu or Memory
                ? Percents.Parse(measure == Cpu ? container.CpuPercent : container.MemPercent)
                : null;
        }

        return measure switch
        {
            Cpu => HostUsage.CpuPercent(_runtime.Value),
            Memory => HostUsage.MemoryPercent(_runtime.Value),
            Disk => HostUsage.DiskPercent(_disk.Value),
            _ => null,
        };
    }

    /// <summary>
    /// Keeps read what these alarms need, for as long as what it gives is
    /// held: the host's runtime, its drive, the containers. The status bar
    /// holds it while it shows them, on any screen.
    /// </summary>
    public IDisposable Follow(IEnumerable<(ObjectInstance Object, string Measure)> alarms)
    {
        var needed = alarms.ToList();
        List<IDisposable> following = [];
        if (needed.Any(a => RegistrySource.Uid(a.Object.Source) is null && a.Measure is Cpu or Memory))
        {
            following.Add(_runtime.Follow());
        }

        if (needed.Any(a => RegistrySource.Uid(a.Object.Source) is null && a.Measure == Disk))
        {
            following.Add(_disk.Follow());
        }

        if (needed.Any(a => RegistrySource.Uid(a.Object.Source) is not null))
        {
            following.Add(_containers.Follow());
        }

        return new Following(following);
    }

    public void Dispose()
    {
        _runtime.Changed -= OnRead;
        _disk.Changed -= OnRead;
        _containers.Changed -= OnRead;
    }

    private void OnRead() => Changed?.Invoke();

    /// <summary>Reads let go of together.</summary>
    private sealed class Following(IReadOnlyList<IDisposable> reads) : IDisposable
    {
        public void Dispose()
        {
            foreach (var read in reads)
            {
                read.Dispose();
            }
        }
    }
}
