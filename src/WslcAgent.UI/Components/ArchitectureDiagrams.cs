namespace WslcAgent.UI.Components;

/// <summary>
/// The Archify diagrams under wwwroot/archify (docs/architecture.md lists them):
/// each one's file name, its title, the word on the window's keys and the
/// screens it belongs to. One list for the button that opens the screen's own
/// and for the window's switch between them, so a diagram is added in one place.
/// </summary>
public static class ArchitectureDiagrams
{
    /// <summary><paramref name="Short"/> is the word on the window's keys, where the full title would not fit four times.</summary>
    public sealed record Diagram(string File, string Title, string Short, params string[] Routes);

    public static readonly IReadOnlyList<Diagram> All =
    [
        new("wslc-overview", "Architecture", "Agent"),
        new("wslc-terminal", "Terminal architecture", "Terminals", "terminal", "containers"),
        new("wslc-networks", "Network architecture", "Networks", "networks"),
        new("wslc-mcp", "Skill and MCP architecture", "Skill and MCP", "settings"),
    ];

    /// <summary>The control plane: what every screen without a diagram of its own opens.</summary>
    public static Diagram Main => All[0];

    /// <summary>
    /// The diagram of the screen at <paramref name="route"/> (its first path segment), or
    /// the main one. Containers takes the terminals': a container's shell is opened there.
    /// </summary>
    public static Diagram ForRoute(string route) =>
        All.FirstOrDefault(d => d.Routes.Contains(route, StringComparer.OrdinalIgnoreCase)) ?? Main;

    public static Diagram ByFile(string file) =>
        All.FirstOrDefault(d => d.File == file) ?? Main;
}
