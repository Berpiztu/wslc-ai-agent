using MudBlazor;
using WslcAgent.ApiClient;
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
    /// <summary>The image check's name: Basque for safekeeper, the one who watches over and protects, here the containers.</summary>
    public const string Name = "Zaindari";

    public const string Title = $"{Name}: check vulnerabilities";

    /// <summary>The name with what it does beside it, where its window shows it: nobody has to have read the README.</summary>
    public const string Label = $"{Name} - Vulnerabilities";

    /// <summary>
    /// The window, in the one logs open in: every image checked now, its
    /// vulnerabilities one after the other. <paramref name="patchable"/>: opened
    /// for one container or one image, it offers the image's patch.
    /// </summary>
    public static Task<bool> ShowAsync(IDialogService dialogs, string about, IReadOnlyList<string> images, bool patchable = false) =>
        DialogFlow.ShowAsync<ImageVulnerabilitiesDialog>(dialogs, $"{Label} — {about}",
            new DialogParameters<ImageVulnerabilitiesDialog> { { d => d.Images, images }, { d => d.About, about }, { d => d.Patchable, patchable } }, large: true);

    /// <summary>
    /// What Update image and View &amp; edit say above their Patch it box: the
    /// image carries a system package with a fix published that it does not
    /// have, so pulling it or recreating on it again does not mend it. Empty
    /// when there is nothing to patch.
    /// </summary>
    public static string PatchNote(ImageVulnerabilities report)
    {
        if (report.Patch.Length == 0 || report.Vulnerabilities.FirstOrDefault(v => v.Ecosystem != "PyPI" && v.FixedIn.Length > 0) is not { } first)
        {
            return "";
        }

        return $"{report.Image} carries a system package with a fix published that it does not have yet ({first.Package} {first.Version}, {first.Severity}, fixed in {first.FixedIn}): "
            + $"pulling it or recreating on it again does not mend it. Patch it builds {report.Patch}, its system packages upgraded, and uses that.";
    }

    /// <summary>The container runs a patch Zaindari built: its chip says patched.</summary>
    public static bool IsPatched(ContainerSummary container) => ImageReference.IsPatched(container.Image);

    /// <summary>Any of a project's containers runs a patch.</summary>
    public static bool IsPatched(IEnumerable<ContainerSummary> containers) => containers.Any(IsPatched);

    /// <summary>The box that asks for the patch, unticked unless the user ticks it.</summary>
    public static string PatchLabel(ImageVulnerabilities report) => $"Patch it ({report.Patch})";

    /// <summary>
    /// The colour an image is shown in, on the Images page and in its menu: red
    /// for a vulnerability high or critical, or an image the agent refuses
    /// (built here, to be built again); the yellow for any other; none when
    /// nothing is known against it.
    /// </summary>
    public static Color Tone(ImageSummary image) => Tone(image.ImageWorst, image.ImageRefused);

    /// <summary>The colour a container's image is offered in.</summary>
    public static Color Tone(ContainerSummary container) => Tone(container.ImageWorst, container.ImageRefused);

    private static Color Tone(string worst, bool refused) =>
        refused || worst is "high" or "critical" ? Color.Error : worst.Length > 0 ? Color.Warning : Color.Default;

    private static readonly string[] SeverityOrder = ["", "low", "moderate", "high", "critical"];

    /// <summary>The worst severity among the containers' images (<see cref="ContainerSummary.ImageWorst"/>): what a project's chip says; empty for none.</summary>
    public static string Worst(IEnumerable<ContainerSummary> containers) =>
        containers.Select(container => container.ImageWorst).MaxBy(severity => Array.IndexOf(SeverityOrder, severity)) ?? "";

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
