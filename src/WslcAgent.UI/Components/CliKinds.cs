using MudBlazor;

namespace WslcAgent.UI.Components;

/// <summary>
/// The groups the Logs page filters by from its rail: the resource a
/// <c>wslc</c> command acts on, or a file it carries in or out of a container, as the agent classifies it
/// (<c>CliTraceEntry.Kind</c>, <c>AgentLogEntry.Kind</c>), and general for
/// every other command and every other entry. Each carries the icon its
/// section has in the navigation, so the rail reads as the sidebar does.
/// </summary>
public static class CliKinds
{
    public static readonly (string Key, string Label, string Icon)[] Groups =
    [
        ("containers", "Containers", Icons.Material.Filled.ViewInAr),
        ("images", "Images", Icons.Material.Filled.Layers),
        ("networks", "Networks", Icons.Material.Filled.Lan),
        ("volumes", "Volumes", Icons.Material.Filled.Storage),
        // A file carried into or out of a container, and its steps: a group of its own, to follow a transfer through.
        // The agent's own update goes here too, as it waits for the transfers;
        // a group of its own would not fit the filter bar.
        ("transfers", "File transfers", Icons.Material.Filled.SwapVert),
        ("general", "General", Icons.Material.Filled.Notes),
    ];

    /// <summary>The label of a kind; an unknown one reads as General.</summary>
    public static string Label(string kind) => Groups.FirstOrDefault(k => k.Key == kind).Label ?? "General";

    /// <summary>
    /// The label in a column that is only as wide as its longest word: File
    /// transfers is Transfers there, so Containers is the longest and the
    /// column loses two characters; the filter
    /// button and a row's detail keep the whole name.
    /// </summary>
    public static string ShortLabel(string kind) => kind == "transfers" ? "Transfers" : Label(kind);

    /// <summary>The kinds a query names, comma-separated, keeping only the ones that exist.</summary>
    public static IReadOnlySet<string> Parse(string? query) =>
        (query ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(kind => Groups.Any(k => k.Key == kind))
            .ToHashSet(StringComparer.Ordinal);
}
