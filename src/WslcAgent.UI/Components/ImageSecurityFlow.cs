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

    /// <summary>A project's Zaindari window: its containers as a menu, and for the one chosen its report, what can be accepted, and its patch.</summary>
    public static Task<bool> ShowProjectAsync(IDialogService dialogs, string project, IReadOnlyList<ContainerSummary> members) =>
        DialogFlow.ShowAsync<ProjectVulnerabilitiesDialog>(dialogs, $"{Label} — {project}",
            new DialogParameters<ProjectVulnerabilitiesDialog> { { d => d.Project, project }, { d => d.Members, members } }, wide: true);

    /// <summary>
    /// The patch editor (<see cref="PatchImageDialog"/>), the same from Zaindari's
    /// window, Update image and View &amp; edit: <paramref name="only"/> ticks those
    /// containers alone (by id or name), none ticks every one of the image's.
    /// True once the patch has started.
    /// </summary>
    public static Task<bool> ShowPatchAsync(IDialogService dialogs, ImageVulnerabilities report, IReadOnlyList<string> only) =>
        DialogFlow.ShowAsync<PatchImageDialog>(dialogs, $"Patch {report.Image}",
            new DialogParameters<PatchImageDialog> { { d => d.Report, report }, { d => d.Only, only } }, large: true);

    /// <summary>
    /// What can be accepted in the image, as <see cref="AcceptVulnerabilitiesDialog"/> lists it:
    /// each Python library's vulnerability moderate or worse that no version fixes
    /// (<see cref="ImageRemedies.Acceptable"/>), the ones that colour the image and nobody
    /// can mend, and every one accepted already, to withdraw. A system package with no
    /// fix colours nothing, so it is not offered.
    /// </summary>
    public static IReadOnlyList<ImageAdvisory> Acceptable(ImageVulnerabilities report) =>
        [.. report.Vulnerabilities.Where(v => v.Accepted is not null
            || (v.Ecosystem == "PyPI" && v.Severity is "moderate" or "high" or "critical" && ImageRemedies.Acceptable(v.FixedIn, v.UbuntuProFixedIn)))];

    /// <summary>The accepting window (<see cref="AcceptVulnerabilitiesDialog"/>); true once something was accepted or withdrawn.</summary>
    public static Task<bool> ShowAcceptAsync(IDialogService dialogs, ImageVulnerabilities report) =>
        DialogFlow.ShowAsync<AcceptVulnerabilitiesDialog>(dialogs, $"Accept vulnerabilities of {ImageReference.RepositoryOf(report.Image)}",
            new DialogParameters<AcceptVulnerabilitiesDialog> { { d => d.Report, report } });

    /// <summary>
    /// What Update image and View &amp; edit say beside their Patch… button: the
    /// image carries a system package with a fix published that it does not
    /// have, so pulling it or recreating on it again does not mend it. Empty
    /// when there is nothing to patch.
    /// </summary>
    public static string PatchNote(ImageVulnerabilities report)
    {
        if (report.SystemUpgrade.Length == 0 || report.Vulnerabilities.FirstOrDefault(v => v.Ecosystem != "PyPI" && v.FixedIn.Length > 0) is not { } first)
        {
            return "";
        }

        return $"{report.Image} carries a system package with a fix published that it does not have yet ({first.Package} {first.Version}, {first.Severity}, fixed in {first.FixedIn}): "
            + $"pulling it or recreating on it again does not mend it. Patch… opens the patch editor: {report.Patch} is built with them upgraded, and anything you add, and the container moved to it.";
    }

    /// <summary>The container runs a patch Zaindari built: its chip says patched.</summary>
    public static bool IsPatched(ContainerSummary container) => ImageReference.IsPatched(container.Image);

    /// <summary>Any of a project's containers runs a patch.</summary>
    public static bool IsPatched(IEnumerable<ContainerSummary> containers) => containers.Any(IsPatched);

    /// <summary>What a container says while a patch of its image is built: that it is patched, or, one the patch does not move, that its image is.</summary>
    public static string PatchStatus(ContainerSummary container, string patch) =>
        container.PatchImageOnly ? $"Patching its image: building {patch}" : $"Patching: building {patch}";

    /// <summary>What a patch would mend, in the one line the offer has: how many system packages have a fix the image does not carry.</summary>
    public static string PatchSummary(ImageVulnerabilities report)
    {
        var mended = report.Vulnerabilities.Where(v => v.Ecosystem != "PyPI" && v.FixedIn.Length > 0)
            .Select(v => v.Package).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        return $"{Name}: a patch can mend {mended} system package{(mended == 1 ? "" : "s")} of {report.Image} that pulling it again would not.";
    }

    /// <summary>
    /// The colour an image is shown in, on the Images page and in its menu: red
    /// for a vulnerability high or critical that something can be done about
    /// (<see cref="ImageSummary.ImageWorst"/>), or an image the agent refuses
    /// (built here, to be built again); the yellow for any other such; none
    /// when nothing is known against it, or nothing anyone downstream can mend.
    /// </summary>
    public static Color Tone(ImageSummary image) => Tone(image.ImageWorst, image.ImageRefused);

    /// <summary>The colour a container's image is offered in.</summary>
    public static Color Tone(ContainerSummary container) => Tone(container.ImageWorst, container.ImageRefused);

    private static Color Tone(string worst, bool refused) =>
        refused || worst is "high" or "critical" ? Color.Error : worst.Length > 0 ? Color.Warning : Color.Default;

    private static readonly string[] SeverityOrder = ["", "low", "moderate", "high", "critical"];

    /// <summary>The worst severity known among the containers' images, mendable or not (<see cref="ContainerSummary.ImageKnown"/>): what a project's chip says; empty for none.</summary>
    public static string Worst(IEnumerable<ContainerSummary> containers) =>
        containers.Select(container => container.ImageKnown).MaxBy(severity => Array.IndexOf(SeverityOrder, severity)) ?? "";

    /// <summary>The worst of the containers' colours: a project's.</summary>
    public static Color Tone(IEnumerable<ContainerSummary> containers)
    {
        var tones = containers.Select(Tone).ToList();
        return tones.Contains(Color.Error) ? Color.Error : tones.Contains(Color.Warning) ? Color.Warning : Color.Default;
    }

    /// <summary>A text in its colour on the page's ground (a menu item, a patch's row): MudBlazor's red, the warning tone the log viewer's warnings wear (the theme's amber did not read on the white), none otherwise.</summary>
    public static string TextClass(Color tone) => tone switch
    {
        Color.Error => "mud-error-text",
        Color.Warning => "wslc-warning-text",
        _ => "",
    };
}
