using Microsoft.JSInterop;

namespace WslcAgent.UI.Components;

/// <summary>
/// Which projects stand open in the Containers list, showing their containers
/// under them: closed until the user opens one, and remembered per project as
/// the table-or-cards choice is (<see cref="ViewPreference"/>), on this device
/// (<c>wslcAgent.groups</c>).
/// </summary>
public sealed class ProjectGroups(IJSRuntime js)
{
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);
    private Task? _loading;

    /// <summary>Reads what this device remembers, once; a host whose browser is not reachable yet tries again on the next visit.</summary>
    public Task ReadyAsync() => _loading ??= LoadAsync();

    public bool IsOpen(string project) => _open.Contains(project);

    /// <summary>Opens a group and leaves it open: something is being done to its containers.</summary>
    public void Open(string project)
    {
        if (_open.Add(project))
        {
            _ = SaveAsync();
        }
    }

    /// <summary>Opens a closed group, closes an open one.</summary>
    public void Toggle(string project)
    {
        if (!_open.Remove(project))
        {
            _open.Add(project);
        }

        _ = SaveAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("wslcAgent.groups");
            _open.UnionWith((stored ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            _loading = null;  // The web view was not there yet: the next visit asks again.
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.groups", string.Join(';', _open));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Storage refused or the web view is gone: the groups still hold for this run.
        }
    }
}
