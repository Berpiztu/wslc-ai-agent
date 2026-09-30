using MudBlazor;
using WslcAgent.ApiClient;
using WslcAgent.UI.Components.Dialogs;

namespace WslcAgent.UI.Components;

/// <summary>
/// The pickers, each once: selecting an image, a volume, a network or a host
/// folder is the same dialog wherever it appears (launch form, Volumes page,
/// Recreate…), and a picker that can create embeds the resource's own
/// create dialog and selects what it created. Each opens positioned on the
/// field's current value.
/// </summary>
public static class Pickers
{
    public static Task<string?> ImageAsync(IDialogService dialogs, WslcAgentApi api, string current = "") =>
        DialogFlow.PickAsync<NamedPickerDialog>(dialogs, "Select image", Parameters(
            async () => (await api.GetImagesAsync()).Images.Where(i => !i.IsDangling).Select(i => i.Reference).ToList(),
            "Local images. Pull brings a new one.", typeof(PullImageDialog), "Pull image", WithTag(current)));

    /// <summary>The list holds <c>repository:tag</c>; a field without a tag means <c>latest</c>.</summary>
    private static string WithTag(string image)
    {
        var (name, tag) = Launch.LaunchForm.SplitImage(image);
        return name.Length == 0 ? "" : $"{name}:{(tag.Length > 0 ? tag : "latest")}";
    }

    public static Task<string?> VolumeAsync(IDialogService dialogs, WslcAgentApi api, string current = "") =>
        DialogFlow.PickAsync<NamedPickerDialog>(dialogs, "Select volume", Parameters(
            async () => (await api.GetVolumesAsync()).Volumes.Select(v => v.Name).ToList(),
            "Choose an existing name, or Create to open the volume form.", typeof(CreateVolumeDialog), "Create volume", current));

    /// <summary>A container by name, every state; nothing to create from here, a container comes from Run.</summary>
    public static Task<string?> ContainerAsync(IDialogService dialogs, WslcAgentApi api, string current = "") =>
        DialogFlow.PickAsync<NamedPickerDialog>(dialogs, "Select container", Parameters(
            async () => (await api.GetContainersAsync(all: true)).Containers.Select(c => c.Name).ToList(),
            "Containers on this machine, running or not.", null, "", current));

    public static Task<string?> NetworkAsync(IDialogService dialogs, WslcAgentApi api, string current = "") =>
        DialogFlow.PickAsync<NamedPickerDialog>(dialogs, "Select network", Parameters(
            async () => (await api.GetNetworksAsync()).Networks.Select(n => n.Name).ToList(),
            "Choose an existing name, or Create to open the network form.", typeof(CreateNetworkDialog), "Create network", current));

    /// <summary>Opens on the folder's parent with the folder selected; an empty or unknown value starts at the drives.</summary>
    public static Task<string?> HostFolderAsync(IDialogService dialogs, string current = "") =>
        DialogFlow.PickAsync<HostFolderPickerDialog>(dialogs, "Select host folder", new DialogParameters<HostFolderPickerDialog> { { d => d.Path, current } });

    private static DialogParameters Parameters(Func<Task<IReadOnlyList<string>>> load, string hint, Type? createDialog, string createTitle, string current) =>
        new DialogParameters<NamedPickerDialog>
        {
            { d => d.Load, load },
            { d => d.Hint, hint },
            { d => d.CreateDialog, createDialog },
            { d => d.CreateTitle, createTitle },
            { d => d.Selected, current },
        };
}
