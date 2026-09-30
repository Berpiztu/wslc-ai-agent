using Microsoft.JSInterop;
using MudBlazor;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// Open with browser, with the ports popup:
/// a tap on a container's ports lists every port and its ways in. Local is a new tab at the agent's machine, the host
/// browser pane anywhere else (and there too when Settings asks to simulate
/// remote access), because 127.0.0.1 is not that machine. Remote is the port's
/// public name (Publish), which opens in a new tab from anywhere. The pane's
/// sessions are joined from View browser sessions.
/// </summary>
public static class BrowserFlow
{
    /// <summary>The UI talks to an agent on another machine: its loopback ports are out of reach.</summary>
    public static bool IsRemote(Uri? agent) =>
        agent?.Host.ToLowerInvariant() is not ("127.0.0.1" or "localhost" or "::1" or "[::1]");

    public static async Task OpenAsync(IDialogService dialogs, ISnackbar snackbar, IJSRuntime js, WslcAgent.UI.Links.IClientLinks links, Uri? agent, SimulatedRemote simulated, string container, IReadOnlyList<string> ports, IReadOnlyList<Publication> publications)
    {
        var published = PublishedPorts.Parse(ports);
        if (published.Count == 0)
        {
            snackbar.Add("No published ports found", Severity.Error);
            return;
        }

        var host = PublishedPorts.HostOf(agent);
        var away = simulated.Enabled || IsRemote(agent);
        var chosen = await DialogFlow.PickAsync<Dialogs.OpenPortDialog>(dialogs, "Open with browser",
            new DialogParameters<Dialogs.OpenPortDialog>
            {
                { d => d.Ports, published },
                { d => d.Host, host },
                { d => d.Publications, publications },
                { d => d.AwayFromAgent, away },
            });
        if (chosen is null)
        {
            return;
        }

        var remote = chosen.StartsWith(Dialogs.OpenPortDialog.Remote, StringComparison.Ordinal);
        var hostPort = chosen[(remote ? Dialogs.OpenPortDialog.Remote : Dialogs.OpenPortDialog.Local).Length..];
        if (published.FirstOrDefault(p => p.HostPort.ToString(System.Globalization.CultureInfo.InvariantCulture) == hostPort) is not { } port)
        {
            return;
        }

        if (remote && PublishedPorts.PublicUrl(port, publications) is { } publicUrl)
        {
            await WslcAgent.UI.Links.ClientLink.OpenAsync(links, js, snackbar, publicUrl);
            return;
        }

        if (away)
        {
            await ShowPaneAsync(dialogs, snackbar, container, hostPort, "");
            return;
        }

        await WslcAgent.UI.Links.ClientLink.OpenAsync(links, js, snackbar, port.Url(host));
    }

    /// <summary>Connect from View browser sessions: the same browser, as one more viewer.</summary>
    public static Task JoinAsync(IDialogService dialogs, ISnackbar snackbar, string container, BrowseSessionInfo session) =>
        ShowPaneAsync(dialogs, snackbar, container, session.HostPort, session.Id);

    private static async Task ShowPaneAsync(IDialogService dialogs, ISnackbar snackbar, string container, string hostPort, string sessionId)
    {
        var joining = sessionId.Length > 0;
        snackbar.Add(joining ? "Joined host browser session" : $"Opened host browser for port {hostPort}", Severity.Success);
        var parameters = new DialogParameters<Dialogs.BrowserPaneDialog>
        {
            { d => d.Container, container },
            { d => d.HostPort, hostPort },
            { d => d.SessionId, sessionId },
        };
        await DialogFlow.ShowFullScreenAsync<Dialogs.BrowserPaneDialog>(dialogs, joining ? $"Browser — join :{hostPort}" : $"Browser — :{hostPort}", parameters);
    }
}
