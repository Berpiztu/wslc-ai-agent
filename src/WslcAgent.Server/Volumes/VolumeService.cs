using System.Text.Json;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Volumes;

/// <summary>Managed volumes as <c>wslc volume list --format json</c> reports them, marked with container usage.</summary>
public sealed class VolumeService(IWslcRunner wslc, ContainerUsageScanner usage, Resources.ResourceRegistry registry) : IVolumeService
{
    private const string NotAvailable = "N/A";

    public async Task<VolumeListResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        var listTask = wslc.RunAsync(["volume", "list", "--format", "json"], cancellationToken: cancellationToken);
        var usageTask = usage.ScanAsync(cancellationToken);
        await Task.WhenAll(listTask, usageTask);

        var rows = WslcJson.ParseRows(listTask.Result.Stdout)
            .Select(row => ToSummary(row, usageTask.Result))
            .ToList();
        // Every read is the registry's too, a volume by its name.
        var uids = registry.Reconcile(Resources.ResourceRegistry.Volume, [.. rows.Select(v => (v.Name, v.Name))], complete: true);
        var volumes = rows.Select(v => v with { Uid = uids.GetValueOrDefault(v.Name) }).ToList();
        var totals = usageTask.Result.Totals;
        return new VolumeListResponse(volumes, volumes.Count, totals.DiskReadBytes, totals.DiskWriteBytes);
    }

    public Task CreateAsync(CreateVolumeRequest request, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(CreateArgs(request), cancellationToken: cancellationToken);

    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await wslc.RunAsync(["volume", "remove", WslcArgs.Require(name, "volume name")], cancellationToken: cancellationToken);
        registry.Removed(Resources.ResourceRegistry.Volume, name);
    }

    public async Task<VolumeInspect> InspectAsync(string name, CancellationToken cancellationToken = default)
    {
        var volume = WslcArgs.Require(name, "volume name");
        var result = await wslc.RunAsync(["volume", "inspect", volume], cancellationToken: cancellationToken);
        var first = WslcJson.ParseRows(result.Stdout).FirstOrDefault();
        var json = first.ValueKind == JsonValueKind.Object ? JsonSerializer.Serialize(first, new JsonSerializerOptions { WriteIndented = true }) : result.Stdout;
        return new VolumeInspect(volume, json);
    }

    /// <summary>
    /// The container rows say which volumes each one mounts but not where, so the
    /// candidates are inspected for the destination; a row whose mounts do not
    /// name the volume is skipped, and a row without mounts is inspected anyway.
    /// One entry per container, sorted by name; the Files helpers are left out.
    /// </summary>
    public async Task<VolumeUsers> UsersAsync(string name, CancellationToken cancellationToken = default)
    {
        var volume = WslcArgs.Require(name, "volume name");
        var list = await wslc.RunAsync(["container", "list", "--all", "--format", "json"], cancellationToken: cancellationToken);
        var candidates = WslcJson.ParseRows(list.Stdout)
            .Where(row => !ContainerService.IsHelper(row.GetString("Names")))
            .Where(row => row.GetString("Mounts") is not { Length: > 0 } mounts
                || mounts.Split(',', StringSplitOptions.TrimEntries).Contains(volume))
            .Select(row => row.GetString("ID") is { Length: > 0 } id ? id : row.GetString("Id"))
            .Where(id => id.Length > 0)
            .ToList();

        var users = new List<VolumeUser>();
        foreach (var id in candidates)
        {
            var inspection = ContainerInspection.Parse(await wslc.RunAsync(["container", "inspect", id, "--format", "json"], cancellationToken: cancellationToken));
            if (inspection.Mounts.FirstOrDefault(m => m.Type == "volume" && m.Source == volume) is { } mount)
            {
                users.Add(new VolumeUser(inspection.Name, inspection.Id, inspection.Image, inspection.State, mount.Destination, mount.Mode != "ro"));
            }
        }

        return new VolumeUsers(volume, users.OrderBy(u => u.Name.Length > 0 ? u.Name : u.Id, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary><c>-f</c>: see <see cref="Images.ImageService.PruneAsync"/> (the 2.9.12 confirmation prompt).</summary>
    public async Task<CommandOutput> PruneAsync(CancellationToken cancellationToken = default) =>
        (await wslc.RunAsync(["volume", "prune", "-f", "-a"], cancellationToken: cancellationToken)).Output;

    /// <summary><c>vhd</c> volumes take their size as the <c>SizeBytes</c> option and <c>Fixed=true</c> to allocate it now; <c>guest</c> ignores both.</summary>
    internal static List<string> CreateArgs(CreateVolumeRequest request)
    {
        var name = WslcArgs.Require(request.Name, "volume name");
        var driver = request.Driver.Trim();
        var args = new List<string> { "volume", "create" }.Option("--driver", driver);
        if (driver == "vhd")
        {
            if (request.Size.Trim().Length > 0)
            {
                var bytes = StatsParsing.Bytes(request.Size);
                if (bytes <= 0)
                {
                    throw new ArgumentException($"Invalid size: {request.Size}", nameof(request));
                }

                args.Option("--opt", $"SizeBytes={bytes}");
            }

            if (request.Fixed)
            {
                args.Option("--opt", "Fixed=true");
            }
        }

        args.Option("--opt", request.Option).Option("--label", request.Label);
        args.Add(name);
        return args;
    }

    internal static VolumeSummary ToSummary(JsonElement row, ContainerUsage usage)
    {
        var name = row.GetString("Name").Trim();
        var use = ContainerUsage.Of(usage.VolumeUse, name);
        return new VolumeSummary(
            Name: name,
            Driver: row.GetString("Driver"),
            Mountpoint: Field(row, "Mountpoint"),
            Scope: Field(row, "Scope"),
            Labels: Field(row, "Labels"),
            InUse: use.Containers > 0,
            Containers: use.Containers,
            Running: use.Running,
            DiskReadBytes: use.DiskReadBytes,
            DiskWriteBytes: use.DiskWriteBytes);
    }

    /// <summary>The CLI prints <c>N/A</c> for what it does not know; that is "empty" here.</summary>
    private static string Field(JsonElement row, string name)
    {
        var value = row.GetString(name).Trim();
        return value == NotAvailable ? "" : value;
    }
}
