using System.Diagnostics;

namespace WslcAgent.Tray;

/// <summary>
/// The agent's icon beside the clock: a click opens the agent's page in the
/// browser, and the right button the menu — Open in browser and Dashboard,
/// the page or the dashboard in the browser; Close icon, which takes the icon
/// away and leaves the agent running. It shows the agent's notifications too,
/// as its toasts (<see cref="NotificationToasts"/>).
/// </summary>
internal sealed class AgentTray : ApplicationContext
{
    /// <summary>The agent's own page: the System card over the log.</summary>
    private const string AgentPage = "agent";

    private readonly NotifyIcon _icon;
    private readonly NotificationToasts _toasts;

    /// <param name="agent">The agent it opens and listens to.</param>
    /// <param name="named">Given on the command line (a development agent): its port goes in the icon's tooltip, so it is not taken for the installed one's.</param>
    public AgentTray(Uri agent, bool named)
    {
        var page = new Uri(agent, AgentPage);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open in browser", null, (_, _) => Browse(page));
        menu.Items.Add("Dashboard", null, (_, _) => Browse(agent));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Close icon", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
            Text = named ? $"WSLC AI Agent · {agent.Port}" : "WSLC AI Agent",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, click) =>
        {
            if (click.Button == MouseButtons.Left)
            {
                Browse(page);
            }
        };
        _toasts = new NotificationToasts(agent, _icon.Text, _icon.Icon);
    }

    /// <summary>An address of the agent's in the browser: the click's, the menu's, and what a notification opens.</summary>
    internal static void Browse(Uri address) =>
        Process.Start(new ProcessStartInfo(address.ToString()) { UseShellExecute = true });

    protected override void ExitThreadCore()
    {
        _toasts.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
