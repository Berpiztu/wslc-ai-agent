using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WslcAgent.Tray;

/// <summary>
/// The agent's window: its own page in
/// WebView2, the same page the browser shows, so there is one UI. Its
/// browsing data lives beside the agent's data, apart from any browser's.
/// </summary>
internal sealed class AgentWindow : Form
{
    private readonly WebView2 _view = new() { Dock = DockStyle.Fill };

    public AgentWindow(Uri page, Icon? icon)
    {
        Text = "WSLC AI Agent";
        Icon = icon;
        ClientSize = new Size(1200, 850);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        Controls.Add(_view);
        Load += async (_, _) =>
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSLC-AI-Agent", "WebView2");
            await _view.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(userDataFolder: data));
            _view.CoreWebView2.Navigate(page.ToString());
        };
    }
}
