using Microsoft.AspNetCore.Components;
using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Updates;

/// <summary>
/// Installing the agent's package, from wherever the user asks for it: the
/// offer's toast and the version badge next to the brand. One place, because
/// the waiting is the hard part of it — an installer is tens of megabytes, and
/// a toast that fades after three seconds left the user in front of a screen
/// where nothing seemed to happen.
/// </summary>
public static class UpdateFlow
{
    /// <summary>
    /// The agent's package downloaded and installed, whether or not it is newer
    /// (the System card's version is already
    /// green or amber, so a tap on it downloads instead of asking again). The
    /// agent is asked first only when this client has not heard from it yet.
    /// </summary>
    public static async Task DownloadAndInstallAsync(ISnackbar snackbar, ClientUpdateChecker checker)
    {
        if (checker.Package is null)
        {
            await checker.CheckAsync(force: true);
        }

        if (checker.Package is not { Available: true } package)
        {
            snackbar.Add(checker.Package?.Error is { Length: > 0 } error ? error : "The agent has no client package to install.", Severity.Warning);
            return;
        }

        await InstallAsync(snackbar, checker, package);
    }

    /// <summary>
    /// The version's one verb, wherever it is shown — the badge and the System
    /// card's footer: amber installs, with the same toast as the offer;
    /// otherwise the agent is asked again, forgotten versions included, and
    /// its answer said.
    /// </summary>
    public static async Task CheckOrInstallAsync(ISnackbar snackbar, ClientUpdateChecker checker)
    {
        if (checker.IsCurrent == false && checker.Package is not null)
        {
            await InstallAsync(snackbar, checker, checker.Package);
            return;
        }

        var newer = await checker.CheckAsync(force: true);
        if (newer is not null)
        {
            snackbar.Add($"New version available ({newer.Version}). Click the version again to install.", Severity.Success);
        }
        else if (checker.Package is { } package)
        {
            snackbar.Add(package.Available ? $"The agent has {package.Version}: this client is up to date." : package.Error, Severity.Info);
        }
        else
        {
            snackbar.Add("The agent did not answer the version check.", Severity.Warning);
        }
    }

    /// <summary>
    /// Downloads and hands the package to the platform installer, counting the
    /// download in one toast that stays until the work ends. Windows closes the
    /// app as the installer starts; Android returns from the package installer,
    /// so the toast is closed before that.
    /// </summary>
    public static async Task InstallAsync(ISnackbar snackbar, ClientUpdateChecker checker, ClientPackageInfo package)
    {
        using var cancel = new CancellationTokenSource();
        var line = new LiveProgress();
        line.Set("Downloading update…");
        var toast = snackbar.Add(
            builder =>
            {
                builder.OpenComponent<LiveProgressToast>(0);
                builder.AddComponentParameter(1, nameof(LiveProgressToast.Progress), line);
                builder.CloseComponent();
            },
            Severity.Info,
            options =>
            {
                options.RequireInteraction = true;
                options.CloseAfterNavigation = false;
                options.ShowCloseIcon = true;
                // The cross is the only way out of a download that is taking too
                // long, so it ends the download instead of hiding it and leaving
                // the installer to open by itself minutes later.
                options.CloseButtonClickFunc = _ =>
                {
                    cancel.Cancel();
                    return Task.CompletedTask;
                };
            });

        var progress = new Progress<DownloadProgress>(read => line.Set($"Downloading update… {read.Text}"));
        try
        {
            await checker.InstallAsync(package, progress, cancel.Token);
            toast?.ForceClose();
            snackbar.Add("Starting the installer…", Severity.Info);
        }
        catch (OperationCanceledException)
        {
            // The cross: the toast is already on its way out, and the half file
            // the download left is dropped by the client that wrote it.
            snackbar.Add("Update cancelled.", Severity.Info);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AgentApiException or HttpRequestException or IOException)
        {
            toast?.ForceClose();
            snackbar.Add(ex.Message, Severity.Error);
        }
    }
}
