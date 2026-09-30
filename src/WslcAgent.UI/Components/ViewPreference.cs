using Microsoft.JSInterop;

namespace WslcAgent.UI.Components;

/// <summary>
/// Table or cards, as the user last chose it <b>for each list</b>: containers
/// can be cards while images stay rows, and coming back to a list shows it the
/// way it was left instead of dropping to rows every time. A page whose address
/// carries a view (<c>?view=cards</c>, what Back and a shared link restore) uses
/// that one, and it becomes the choice for that list.
/// <para>
/// The choice belongs to the client, not to the agent: it lives in this device's
/// storage (<c>wslcAgent.views</c>), so closing the app and opening it again
/// finds every list as it was left, and two clients of the same agent each keep
/// their own.
/// </para>
/// </summary>
public sealed class ViewPreference(IJSRuntime js)
{
    private readonly Dictionary<string, ViewMode> _bySection = new(StringComparer.OrdinalIgnoreCase);
    private Task? _loading;

    /// <summary>
    /// Reads what this device remembers, once. Awaited before a list decides how
    /// to draw itself; a host where the browser is not reachable yet (the native
    /// clients, whose web view loads after the first components) gets nothing and
    /// tries again on the next list, instead of losing the choice for the session.
    /// </summary>
    public Task ReadyAsync() => _loading ??= LoadAsync();

    /// <summary>How that list was last shown; cards until the user says otherwise.</summary>
    public ViewMode For(string section) => _bySection.GetValueOrDefault(section, ViewMode.Cards);

    public void Set(string section, ViewMode view)
    {
        if (For(section) == view && _bySection.ContainsKey(section))
        {
            return;
        }

        _bySection[section] = view;
        _ = SaveAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("wslcAgent.views");
            foreach (var pair in (stored ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=');
                if (parts.Length == 2 && Enum.TryParse<ViewMode>(parts[1], ignoreCase: true, out var view))
                {
                    _bySection[parts[0]] = view;
                }
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            _loading = null;  // The web view was not there yet: the next list asks again.
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            await js.InvokeAsync<string?>("wslcAgent.views", string.Join(';', _bySection.Select(pair => $"{pair.Key}={pair.Value}")));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Storage refused or the web view is gone: the choice still holds for this run.
        }
    }
}
