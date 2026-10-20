using MudBlazor;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>
/// What a container's, a project's and an image's menus share about View image
/// packages and CVEs: the window it opens, and the colour it is offered in,
/// the cards' own: red when the agent makes no container from the image or
/// it has a vulnerability high or critical, the yellow when it has known
/// vulnerabilities and is not stopped, none when nothing is known against it.
/// </summary>
public static class ImageSecurityFlow
{
    public const string Title = "View image packages and CVEs";

    /// <summary>The window, in the one logs open in, every image's libraries and vulnerabilities one after the other.</summary>
    public static Task<bool> ShowAsync(IDialogService dialogs, string about, IReadOnlyList<string> images) =>
        DialogFlow.ShowAsync<ImageVulnerabilitiesDialog>(dialogs, $"Packages and CVEs — {about}",
            new DialogParameters<ImageVulnerabilitiesDialog> { { d => d.Images, images } }, large: true);

    /// <summary>The colour a container's image is offered in.</summary>
    public static Color Tone(ContainerSummary container) =>
        container.ImageRefused || container.HasSevereImage ? Color.Error : container.HasImageWarning ? Color.Warning : Color.Default;

    private static readonly string[] SeverityOrder = ["", "low", "moderate", "high", "critical"];

    /// <summary>The worst severity among the containers' images (<see cref="ContainerSummary.ImageWorst"/>): what a project's chip says; empty for none.</summary>
    public static string Worst(IEnumerable<ContainerSummary> containers) =>
        containers.Select(container => container.ImageWorst).MaxBy(severity => Array.IndexOf(SeverityOrder, severity)) ?? "";

    /// <summary>A severity's colour: red for a high or a critical one, the yellow for any other.</summary>
    public static Color SeverityTone(string severity) => severity is "high" or "critical" ? Color.Error : Color.Warning;

    /// <summary>The worst of the containers' colours: a project's.</summary>
    public static Color Tone(IEnumerable<ContainerSummary> containers)
    {
        var tones = containers.Select(Tone).ToList();
        return tones.Contains(Color.Error) ? Color.Error : tones.Contains(Color.Warning) ? Color.Warning : Color.Default;
    }

    /// <summary>The menu item's text in its colour: MudBlazor's text classes for red and the warning tone, none otherwise.</summary>
    public static string TextClass(Color tone) => tone switch
    {
        Color.Error => "mud-error-text",
        Color.Warning => "mud-warning-text",
        _ => "",
    };
}
