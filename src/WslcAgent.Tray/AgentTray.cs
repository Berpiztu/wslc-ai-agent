using System.Diagnostics;

namespace WslcAgent.Tray;

/// <summary>
/// The agent's icon beside the clock: a click
/// shows the agent's page in a window of its own, and the right button the
/// menu of what the click does not — Open in browser and Dashboard, the page
/// or the dashboard in the browser; Close icon, which takes the icon away and
/// leaves the agent running. It shows the agent's notifications too, as its
/// toasts (<see cref="NotificationToasts"/>).
/// </summary>
internal sealed class AgentTray : ApplicationContext
{
    /// <summary>The agent's own page: the System card over the log.</summary>
    private const string AgentPage = "agent";

    private readonly Uri _agent;
    private readonly NotifyIcon _icon;
    private readonly NotificationToasts _toasts;
    private AgentWindow? _window;

    /// <param name="agent">The agent it opens and listens to.</param>
    /// <param name="named">Given on the command line (a development agent): its port goes in the icon's tooltip, so it is not taken for the installed one's.</param>
    public AgentTray(Uri agent, bool named)
    {
        _agent = agent;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open in browser", null, (_, _) => Browse(new Uri(_agent, AgentPage)));
        menu.Items.Add("Dashboard", null, (_, _) => Browse(_agent));
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
                ShowWindow();
            }
        };
        _toasts = new NotificationToasts(_agent, _icon.Text, _icon.Icon);
    }

    /// <summary>The agent's window, maximized and brought to the front, made when there is none.</summary>
    private void ShowWindow()
    {
        if (_window is not { IsDisposed: false })
        {
            _window = new AgentWindow(new Uri(_agent, AgentPage), _icon.Icon);
        }

        _window.Show();
        _window.WindowState = FormWindowState.Maximized;
        _window.Activate();
    }

    /// <summary>An address of the agent's in the browser: the menu's, and what a notification opens.</summary>
    internal static void Browse(Uri address) =>
        Process.Start(new ProcessStartInfo(address.ToString()) { UseShellExecute = true });

    protected override void ExitThreadCore()
    {
        _toasts.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _window?.Dispose();
        base.ExitThreadCore();
    }
}
