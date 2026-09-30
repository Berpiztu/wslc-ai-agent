using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.ClientPackages;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Notifications;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Updates;

/// <summary>
/// The agent updating itself from the installer in its package folder, the one
/// copied there: copying it is the whole deployment, since an
/// install over SSH lands in the SSH account and not in the session the agent
/// runs in. Only the installed agent does it, and never over a transfer — a file
/// on its way or a backup being saved would be cut, so the update waits for
/// them.
/// <para>
/// With Auto update on, a newer installer is announced to every client, which
/// counts a minute down and offers Cancel — and as a notification, whose own
/// button cancels it without opening anything, to every device that gets them
/// (ten seconds was too short for a phone);
/// nobody cancelling, it installs, and a cancel is notified to all of them.
/// A version cancelled, or one whose install failed, is not offered again on
/// its own: Update now still takes it. Update now installs at once, as soon as
/// nothing is being transferred.
/// </para>
/// </summary>
public sealed class AgentUpdater(
    AgentUpdateSettingsStore settings,
    PackageFolders folders,
    IAgentInfo info,
    ContainerTransfers transfers,
    IContainerBackups backups,
    WslcEvents events,
    AgentUpdateLauncher launcher,
    Notifier notifier,
    TimeProvider time,
    ILogger<AgentUpdater> logger) : BackgroundService
{
    public const string PackageName = "wslc-ai-agent.msi";

    /// <summary>
    /// What every line the update writes to the agent's log begins with, and
    /// what puts it under File transfers on the Logs page (AgentLogs.KindOf):
    /// the update waits for the transfers and is followed beside them, not in a
    /// group of its own, which the filter bar has no room for.
    /// </summary>
    public const string LogPrefix = "update: ";

    private static readonly TimeSpan Poll = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PollWhileWaiting = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Countdown = TimeSpan.FromMinutes(1);

    /// <summary>How long a new agent looks for the installer's result, and how often (<see cref="AwaitResultAsync"/>).</summary>
    private static readonly TimeSpan ResultWait = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ResultLook = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long the installer has to be left alone before it counts: a file
    /// still being copied in reads as a broken installer, and one installed half
    /// copied fails.
    /// </summary>
    private static readonly TimeSpan Settled = TimeSpan.FromSeconds(30);

    private readonly bool _installed = InstalledAgentSettings.IsInstalledAgent();
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _wake = new(0);
    private string _state = AgentUpdateState.Idle;
    private bool _requested;
    private DateTimeOffset _startsAt;

    /// <summary>The version cancelled, or whose install failed: not announced again on its own.</summary>
    private string _declined = "";

    private AgentUpdateResult? _last;

    public AgentUpdateStatus Status()
    {
        var package = Available();
        lock (_gate)
        {
            var left = _state == AgentUpdateState.Announced ? (int)Math.Ceiling(Math.Max(0, (_startsAt - time.GetUtcNow()).TotalSeconds)) : 0;
            return new AgentUpdateStatus(settings.Get(), info.Version, _installed, package?.Version ?? "", package is not null && IsNewer(package.Version),
                InFlight, _state, left, _last?.Text ?? "", folders.Folder, folders.Automatic);
        }
    }

    public AgentUpdateStatus Save(AgentUpdateSettings chosen)
    {
        var folder = (chosen.PackageFolder ?? "").Trim();
        if (folder.Length > 0 && !Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(folder)))
        {
            throw new ArgumentException($"The package folder has to be a full path, such as {PackageFolders.DefaultFolder}.");
        }

        settings.Set(chosen with { PackageFolder = folder });
        folders.EnsureExists();
        Wake();
        return Status();
    }

    /// <summary>Update now: refused, with the reason, when there is nothing to update to.</summary>
    public AgentUpdateStatus Request()
    {
        if (!_installed)
        {
            throw new InvalidOperationException("This agent is not the installed one (it runs from a build folder), so no installer replaces it.");
        }

        var package = Available() ?? throw new InvalidOperationException($"There is no agent installer ({PackageName}) in the package folder.");
        // One still being copied in is taken on trust: the look once it has
        // settled decides, and drops the request if it is not newer after all.
        if (package.Settled && !IsNewer(package.Version))
        {
            throw new InvalidOperationException($"The agent is already at {info.Version}; the installer in the package folder is {package.Version}.");
        }

        lock (_gate)
        {
            _requested = true;
        }

        Wake();
        return Status();
    }

    /// <summary>Stops an update that has not started yet: a countdown, or one waiting for the transfers.</summary>
    public AgentUpdateStatus Cancel()
    {
        string? cancelled = null;
        lock (_gate)
        {
            if (_state is AgentUpdateState.Announced or AgentUpdateState.Waiting)
            {
                _declined = Available()?.Version ?? "";
                _requested = false;
                logger.LogInformation(LogPrefix + "The update of the agent to {Version} was cancelled; Update now, in Settings › Update, still takes it", _declined);
                Become(AgentUpdateState.Idle);
                cancelled = _declined;
            }
        }

        // Every device that was told it was coming is told it is not: the
        // one that cancelled, and the others.
        if (cancelled is not null)
        {
            notifier.Raise(NotificationKind.UpdateCancelled, NotificationSeverity.Info, "Agent update cancelled",
                $"The update to {cancelled} was cancelled; Update now, in Settings › Update, still installs it.", NotificationLink.Settings);
        }

        return Status();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = AwaitResultAsync(stoppingToken);
        if (!_installed)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _wake.WaitAsync(Step(), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One look at where things stand and the next move; answers how long until the next look.</summary>
    private TimeSpan Step()
    {
        var package = Available();
        lock (_gate)
        {
            if (_state == AgentUpdateState.Installing)
            {
                return Poll;
            }

            if (package is { Settled: false })
            {
                return PollWhileWaiting;
            }

            var wanted = package is not null && IsNewer(package.Version)
                && (_requested || (settings.Get().AutoUpdate && package.Version != _declined));
            if (!wanted)
            {
                _requested = false;
                Become(AgentUpdateState.Idle);
                return Poll;
            }

            if (InFlight > 0)
            {
                if (_state != AgentUpdateState.Waiting)
                {
                    logger.LogInformation(LogPrefix + "The update of the agent to {Version} waits for {Count} file transfer(s) or backup(s) to finish", package!.Version, InFlight);
                }

                Become(AgentUpdateState.Waiting);
                return PollWhileWaiting;
            }

            if (!_requested)
            {
                if (_state != AgentUpdateState.Announced)
                {
                    _startsAt = time.GetUtcNow() + Countdown;
                    logger.LogWarning(LogPrefix + "The agent updates itself to {Version} in {Seconds} seconds unless a client cancels it", package!.Version, Countdown.TotalSeconds);
                    Become(AgentUpdateState.Announced);
                    notifier.Raise(NotificationKind.UpdateAnnounced, NotificationSeverity.Warning, "Agent update in a minute",
                        $"The agent updates itself from {info.Version} to {package!.Version} in a minute, unless it is cancelled.",
                        NotificationLink.Settings, action: NotificationAction.CancelUpdate);
                    return Countdown;
                }

                var left = _startsAt - time.GetUtcNow();
                if (left > TimeSpan.Zero)
                {
                    return left;
                }
            }

            Install(package!);
            return Poll;
        }
    }

    /// <summary>Called under <see cref="_gate"/>.</summary>
    private void Install(Package package)
    {
        Become(AgentUpdateState.Installing);
        try
        {
            launcher.Launch(package.Path, package.Version);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _requested = false;
            _declined = package.Version;
            _last = new AgentUpdateResult(false, -1, package.Version, exception.Message);
            logger.LogError(LogPrefix + "The update of the agent to {Version} could not start: {Error}", package.Version, exception.Message);
            notifier.Raise(NotificationKind.UpdateFailed, NotificationSeverity.Error, "Agent update failed",
                $"The update to {package.Version} could not start: {exception.Message}", NotificationLink.Settings);
            Become(AgentUpdateState.Idle);
        }
    }

    /// <summary>
    /// The installer's result, looked for until it is there. The update script
    /// writes it once msiexec returns, and the MSI starts the new agent before
    /// that: one look at start-up found nothing, and "Agent updated" was never
    /// said, nor Settings' Last update. So the
    /// file is looked for every two seconds for the first two minutes.
    /// </summary>
    private async Task AwaitResultAsync(CancellationToken stoppingToken)
    {
        var until = time.GetUtcNow() + ResultWait;
        try
        {
            while (!ReportResult() && time.GetUtcNow() < until)
            {
                await Task.Delay(ResultLook, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The agent is stopping.
        }
    }

    /// <summary>
    /// The installer's result, said once, in the log, in Settings and as a
    /// notification: a failure, whose file stays so the version is not offered
    /// again, only when it is new; a success of this very version, whose file
    /// goes. False while there is no result to say.
    /// </summary>
    private bool ReportResult()
    {
        if (AgentUpdateResult.Read(launcher.ResultPath) is not { } result)
        {
            return false;
        }

        lock (_gate)
        {
            _last = result;
            if (!result.Ok)
            {
                _declined = result.Version;
            }
        }

        if (!result.Ok)
        {
            logger.LogError(LogPrefix + "{Result}", result.Text);
            if (JustWritten(launcher.ResultPath))
            {
                notifier.Raise(NotificationKind.UpdateFailed, NotificationSeverity.Error, "Agent update failed", result.Text, NotificationLink.Settings);
            }
        }
        else if (result.Version == info.Version)
        {
            logger.LogInformation(LogPrefix + "{Result}", result.Text);
            File.Delete(launcher.ResultPath);
            notifier.Raise(NotificationKind.UpdateInstalled, NotificationSeverity.Info, "Agent updated", result.Text, NotificationLink.Settings);
        }

        return true;
    }

    /// <summary>
    /// Whether the installer wrote its result a moment ago: a failed result
    /// stays on disk, so the version is not offered again, and every later
    /// start of the agent reads it once more — which is not a new failure.
    /// </summary>
    private bool JustWritten(string path) =>
        time.GetUtcNow().UtcDateTime - File.GetLastWriteTimeUtc(path) < TimeSpan.FromMinutes(10);

    /// <summary>Every client hears of a change of state, so each one shows or takes down its countdown. Called under <see cref="_gate"/>.</summary>
    private void Become(string state)
    {
        if (_state != state)
        {
            _state = state;
            events.Publish(new ChangeNotice([ChangeNotice.AgentUpdate]));
        }
    }

    private void Wake() => _wake.Release();

    private int InFlight => transfers.InFlight + backups.InFlight;

    private bool IsNewer(string version) => ClientPackageInfo.CompareDisplayVersion(version, info.Version) > 0;

    /// <summary>The agent installer in the package folder, the version it carries, and whether nothing has written it for a while.</summary>
    private Package? Available()
    {
        var path = folders.Locate(PackageName);
        if (path is null)
        {
            return null;
        }

        var settled = time.GetUtcNow() - File.GetLastWriteTimeUtc(path) >= Settled;
        var version = MsiProductVersion.Read(path);
        return version.Length > 0 || !settled ? new Package(path, version, settled) : null;
    }

    private sealed record Package(string Path, string Version, bool Settled);
}
