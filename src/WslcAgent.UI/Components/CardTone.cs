using MudBlazor;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>
/// The tone a card's header and footer rows take to say the row's state at a
/// glance: a palette colour as a pastel over the surface, the same kind of mix
/// as the rows' own tone (the primary at 18 %). A running container's is green,
/// in the list's card and in its header and footer on the dashboard alike.
/// </summary>
public static class CardTone
{
    /// <summary>How much of the colour the pastel takes over the surface.</summary>
    private const int Share = 25;

    /// <summary>A container's tone: green while it runs; none otherwise, the rows keeping their own.</summary>
    public static Color? Of(ContainerSummary container) => container.IsRunning ? Color.Success : null;

    /// <summary>The style that paints a row in the tone; null for none, the row keeping its own.</summary>
    public static string? Style(Color? tone) => tone is { } color
        ? $"background-color: color-mix(in srgb, var(--mud-palette-{color.ToString().ToLowerInvariant()}) {Share}%, var(--mud-palette-surface))"
        : null;
}
