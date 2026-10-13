using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// What this client shows its user as a warning or an error, written in the
/// agent's log too. A toast, a line of the title bar or a window's own red
/// text is on one screen for a moment; the log is where it is looked for
/// afterwards, by whoever saw it go or by somebody else. Nothing that is
/// shown as a warning or an error is left out of it (the owner's rule of 13
/// October 2026), whether the agent said it first or the client found it by
/// itself, as a browser that cannot read a folder does.
/// <para>
/// Said to the agent and forgotten: a report that cannot be made (the agent
/// does not answer, which the layout already says) must not become another
/// error on the screen, and one more thing to report.
/// </para>
/// </summary>
public sealed class ClientReports(WslcAgentApi api, NavigationManager navigation, AgentLink link)
{
    public void Warning(string text) => Tell(ClientReport.Warning, text);

    public void Error(string text) => Tell(ClientReport.Error, text);

    private void Tell(string level, string text)
    {
        if (text.Trim().Length > 0 && link.Online)
        {
            _ = SendAsync(new ClientReport(level, text.Trim(), Where()));
        }
    }

    /// <summary>Which client, and the page it was on: what tells one report from another in the log.</summary>
    private string Where() =>
        $"{(OperatingSystem.IsBrowser() ? "web" : "native client")}, /{navigation.ToBaseRelativePath(navigation.Uri)}";

    private async Task SendAsync(ClientReport report)
    {
        try
        {
            await api.ReportClientAsync(report);
        }
        catch (Exception)
        {
            // Not said: see the class's own words.
        }
    }
}
