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
    public const int Share = 25;

    /// <summary>A container's tone: green while it runs; none otherwise, the rows keeping their own.</summary>
    public static Color? Of(ContainerSummary container) => container.IsRunning ? Color.Success : null;

    /// <summary>
    /// How much of the colour a card's body takes: half the rows' share. A
    /// body holds text to read, and it stands between two rows that are
    /// toned already, which it has to stay lighter than.
    /// </summary>
    public const int Soft = 12;

    /// <summary>
    /// A body that raises an alarm, in MudBlazor's own colour, unmixed and the
    /// same in both themes, with black ink: red (<c>Colors.Red.Default</c>)
    /// for a container that failed, or a project with one; yellow
    /// (<c>Colors.Yellow.Default</c>) for one whose image has known
    /// vulnerabilities, a yellow meant to bother until they are mended. Black
    /// is the ink that reads on both: on the red, 5.7 to 1 against white's 3.7
    /// (WCAG's 4.5 for text); on the yellow, 16.6. Null for any other tone,
    /// which is mixed (<see cref="Style"/>). With <see cref="AlarmClass"/>,
    /// whose rules give what sits on it that ink too.
    /// </summary>
    public static string? Alarm(Color? tone) => tone switch
    {
        Color.Error => $"background-color: {Colors.Red.Default}; color: {Colors.Shades.Black}",
        Color.Warning => $"background-color: {Colors.Yellow.Default}; color: {Colors.Shades.Black}",
        _ => null,
    };

    /// <summary>The class that goes with <see cref="Alarm"/>: the links, the secondary and error texts and the dials' track in its black.</summary>
    public const string AlarmClass = "wslc-card-alarm";

    /// <summary>
    /// A project's body when none of its containers raises an alarm: the
    /// colour of the bracket that joins it to its containers in the list,
    /// unmixed and the same in both themes, with white ink (black is the
    /// alarms'): the theme's Primary, indigo, while it is stopped
    /// (<paramref name="tone"/> Primary), its Success, green, while it runs
    /// (Success). Null for any other tone. With <see cref="ProjectClass"/>,
    /// whose rules give its list, its +/− and its dials that white too. The
    /// mixed pink it had read as a faded red on the dark theme.
    /// </summary>
    public static string? Project(Color? tone) => tone is Color.Primary or Color.Success
        ? $"background-color: var(--mud-palette-{tone.Value.ToString().ToLowerInvariant()}); color: #ffffff"
        : null;

    /// <summary>The class that goes with <see cref="Project"/>.</summary>
    public const string ProjectClass = "wslc-card-project";

    /// <summary>The style that paints a row in the tone, or with <paramref name="share"/> a body in a lighter one; null for none, the row keeping its own.</summary>
    public static string? Style(Color? tone, int share = Share) => tone is { } color
        ? $"background-color: color-mix(in srgb, var(--mud-palette-{color.ToString().ToLowerInvariant()}) {share}%, var(--mud-palette-surface))"
        : null;
}
