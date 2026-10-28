using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Backups;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Volumes;

/// <summary>
/// A volume's backups and restores (<see cref="BackupService"/>). A guest
/// volume lives in the session's own virtual disk, with every other, so it is
/// read from inside: a helper of its own (<see cref="FilesHelpers.StartVolumeWorkAsync"/>,
/// <see cref="HelperImage"/>) mounts it, packs it into a <c>.tar.gz</c>, its
/// sockets left out, and the archive is copied out to the host folder; a
/// restore copies the archive in, checks it whole, empties the volume and
/// unpacks it, owners kept by their numbers. A container's backup packs and
/// unpacks its volumes the same way (<see cref="PackAsync"/>, <see cref="UnpackAsync"/>).
/// Either is done with every container that uses the volume stopped, and none
/// of them starts until it is over (<see cref="BusyVolumes"/>): a backup stops
/// those running, as its settings say, and starts them again (<see cref="BackupStops"/>);
/// a restore is refused while one runs.
/// </summary>
public sealed class VolumeBackups(IWslcRunner wslc, FilesHelpers helpers, IVolumeService volumes, BusyVolumes busy, BackupStops stops, BackupBase backupBase, WslcEvents events, AgentLogs agentLogs, IOptions<WslcOptions> options, ILogger<VolumeBackups> logger)
    : BackupService("volume", ChangeNotice.Volume, wslc, backupBase, events, agentLogs, options.Value.DataDirectory, logger), IVolumeBackups
{
    /// <summary>The helper's image: small, with <c>tar</c> and <c>gzip</c>; pulled the first time it is needed.</summary>
    internal const string HelperImage = "alpine:3.24";

    /// <summary>A volume backup's extension; the host file picker filters by its last part.</summary>
    internal const string Extension = ".tar.gz";

    /// <summary>Where the archive is packed inside the helper, on its way out.</summary>
    private const string Archive = "/tmp/wslc-volume-backup.tar.gz";

    /// <summary>The volume's sockets, listed in the helper for tar to leave out.</summary>
    private const string Sockets = "/tmp/wslc-volume-sockets";

    /// <summary>
    /// The folder a backup is copied into inside the helper, on its way in:
    /// <c>container cp</c> copies into a folder there, never onto a file that
    /// is not there yet, and the backup keeps its own name in it.
    /// </summary>
    private const string Incoming = "/tmp";

    /// <summary>
    /// What a volume's backups and restores write in the agent's log begins
    /// with, the volume after it (<c>backup: hermes-data: INFO: Packing
    /// hermes-data.</c>): the Logs page's Backups group.
    /// </summary>
    public const string Prefix = "backup: ";

    public override string LogPrefix => Prefix;

    protected override string BaseSubfolder => "Volumes";

    protected override string FormerBaseSubfolder => "Volume backups";

    public override async Task<BackupPreview> PreviewAsync(string name, string what, CancellationToken cancellationToken)
    {
        var volume = WslcArgs.Require(name, "volume name");
        return new BackupPreview(BackupWhat.Volume, await stops.RunningUsersAsync([volume], cancellationToken), Settings().StopOf(volume));
    }

    public BackupJob StartBackup(string volume, string folder, bool stop)
    {
        var name = WslcArgs.Require(volume, "volume name");
        var settings = Settings();
        var directory = FullPath((folder ?? "").Trim().Length > 0 ? folder! : settings.FolderOf(name), "backup folder");
        var file = Path.Combine(directory, $"{Stamp()}{Extension}");
        return Start(BackupJob.Backup, name, BackupWhat.Volume, file, async (job, ct) =>
        {
            var running = await stops.RunningUsersAsync([name], ct);
            await stops.StoppedAsync(job, name, [name], running, settings.StopOf(name), stop, () => PackAsync(job, name, file, ct), ct);
            var bytes = new FileInfo(file).Length;
            Record(new BackupEntry(name, file, BackupWhat.Volume, bytes, DateTimeOffset.Now, true));
        });
    }

    public BackupJob StartRestore(string file, string volume)
    {
        var name = WslcArgs.Require(volume, "volume name");
        var archive = WslcArgs.Require(file, "backup file");
        if (!Path.IsPathFullyQualified(archive) || !File.Exists(archive))
        {
            throw new ArgumentException($"{archive} is not a file of this machine.");
        }

        return Start(BackupJob.Restore, name, BackupWhat.Volume, archive, async (job, ct) =>
        {
            await UnpackAsync(job, name, archive, ct);
            RecordRestored(name, archive, BackupWhat.Volume, new FileInfo(archive).Length);
        });
    }

    /// <summary>
    /// <paramref name="volume"/> packed into <paramref name="file"/>, its
    /// folder made when it is not there; what was written of it goes when it
    /// fails or is cancelled. A socket (a running program's, made again when
    /// it starts) is left out, and said so: tar cannot keep one.
    /// </summary>
    internal async Task PackAsync(BackupWork job, string volume, string file, CancellationToken cancellationToken)
    {
        await volumes.InspectAsync(volume, cancellationToken);
        await RequireStoppedAsync(volume, "back it up", cancellationToken);
        using var held = busy.Hold(volume, "backed up");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var helper = await HelperAsync(job, volume, cancellationToken);
        try
        {
            job.Step($"Listing the sockets of {volume}, which a backup leaves out");
            await StepAsync(job, ["exec", helper, "sh", "-c", $"cd {FilesHelpers.VolumeMount} && find . -type s | tee {Sockets}"], cancellationToken);
            job.Step($"Packing {volume}");
            await StepAsync(job, ["exec", helper, "tar", "czf", Archive, "-X", Sockets, "-C", FilesHelpers.VolumeMount, "."], cancellationToken);
            job.Step($"Copying it to {file}");
            await StepAsync(job, ["container", "cp", $"{helper}:{Archive}", file], cancellationToken);
            job.Say($"INFO: {file} written, {new FileInfo(file).Length:N0} bytes.");
        }
        catch
        {
            TryDelete(file);
            throw;
        }
        finally
        {
            await helpers.RemoveHelperAsync(helper);
        }
    }

    /// <summary>
    /// <paramref name="file"/> put back into <paramref name="volume"/>: the
    /// volume created when it is not there, refused while a running container
    /// uses it; the archive read whole before anything of the volume goes, so a
    /// damaged one leaves it as it was.
    /// </summary>
    internal async Task UnpackAsync(BackupWork job, string volume, string file, CancellationToken cancellationToken)
    {
        if ((await volumes.ListAsync(cancellationToken)).Volumes.All(existing => existing.Name != volume))
        {
            job.Step($"Creating volume {volume}");
            await volumes.CreateAsync(new CreateVolumeRequest(volume), cancellationToken);
            // Its card comes now, the ring turning on it, not when the restore ends.
            Notice(ChangeNotice.Volume);
        }
        else
        {
            await RequireStoppedAsync(volume, "restore", cancellationToken);
        }

        using var held = busy.Hold(volume, "restored");
        var helper = await HelperAsync(job, volume, cancellationToken);
        try
        {
            job.Step($"Copying {file} in");
            await StepAsync(job, ["container", "cp", file, $"{helper}:{Incoming}"], cancellationToken);
            var archive = Quoted($"{Incoming}/{Path.GetFileName(file)}");
            job.Step("Checking the backup");
            await StepAsync(job, ["exec", helper, "sh", "-c", $"tar tzf {archive} > /dev/null"], cancellationToken);
            job.Step($"Emptying {volume} and unpacking the backup into it");
            await StepAsync(job, ["exec", helper, "sh", "-c",
                $"find {FilesHelpers.VolumeMount} -mindepth 1 -delete && tar xzf {archive} --numeric-owner -C {FilesHelpers.VolumeMount}"], cancellationToken);
            job.Say($"INFO: {volume} now holds what {file} has.");
        }
        finally
        {
            await helpers.RemoveHelperAsync(helper);
        }
    }

    /// <summary>
    /// A volume is backed up or restored with every container that uses it
    /// stopped: one running writes in it meanwhile, half in the backup, or
    /// over the restore. Refused, saying which to stop.
    /// </summary>
    private async Task RequireStoppedAsync(string volume, string verb, CancellationToken cancellationToken)
    {
        if ((await volumes.UsersAsync(volume, cancellationToken)).Containers.Where(user => user.State == "running").Select(user => user.Name).ToList() is { Count: > 0 } running)
        {
            throw new InvalidOperationException($"{volume} is in use by {string.Join(", ", running)}: stop {(running.Count == 1 ? "it" : "them")} first, then {verb}.");
        }
    }

    /// <summary>The job's helper, its image pulled when this machine does not have it; a cancel removes it.</summary>
    private async Task<string> HelperAsync(BackupWork job, string volume, CancellationToken cancellationToken)
    {
        job.Step($"Starting a helper ({HelperImage}) with {volume} mounted");
        var helper = await helpers.StartVolumeWorkAsync(volume, HelperImage, StepTimeout, cancellationToken);
        job.Cancelling(() => helpers.RemoveHelperAsync(helper));
        return helper;
    }
}
