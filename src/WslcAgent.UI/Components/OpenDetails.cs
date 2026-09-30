namespace WslcAgent.UI.Components;

/// <summary>
/// The details page the Containers section is showing, kept while the user is
/// away in another section: the sidebar's Containers entry returns to it, instead
/// of landing on the list and losing the container being worked on. The list
/// itself clears it — arriving there, by the details header's ← Containers or
/// by any other route, is how the section goes back to being the list.
/// </summary>
public sealed class OpenDetails
{
    /// <summary>The relative address of the open details page; empty when the section is the list.</summary>
    public string Containers { get; private set; } = "";

    /// <summary>Raised when the entry to return to changed, so the navigation redraws.</summary>
    public event Action? Changed;

    /// <summary>The Containers section is showing this details page.</summary>
    public void SetContainer(string id) => Set($"containers/{Uri.EscapeDataString(id)}");

    /// <summary>The Containers section is the list again.</summary>
    public void ClearContainers() => Set("");

    private void Set(string url)
    {
        if (Containers == url)
        {
            return;
        }

        Containers = url;
        Changed?.Invoke();
    }
}
