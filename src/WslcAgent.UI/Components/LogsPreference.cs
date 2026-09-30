using System.Globalization;
using Microsoft.JSInterop;

namespace WslcAgent.UI.Components;

/// <summary>
/// The Logs page as the user left it: which types each rail hides, the level
/// chosen, whether each Autorefresh is on and how tall the grid is. Leaving for
/// another screen and coming back finds all of it in place instead of asking
/// to be set up again. As the table-or-cards choice, it belongs to the client
/// and lives in this device's storage (<c>wslcAgent.logs</c>), so it also
/// survives closing the app. An address that carries a filter (Back, a link)
/// wins over what is remembered for that filter.
/// </summary>
public sealed class LogsPreference(IJSRuntime js)
{
    private const string HideKey = "hide";
    private const string HideLogKey = "hidelog";
    private const string LevelKey = "level";
    private const string FollowCliKey = "followCli";
    private const string FollowLogKey = "followLog";
    private const string GridKey = "grid";

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private Task? _loading;

    /// <summary>Reads what this device remembers, once; a host whose browser is not reachable yet tries again on the next visit.</summary>
    public Task ReadyAsync() => _loading ??= LoadAsync();

    /// <summary>The types the Activity list hides, comma-separated; empty when every type is shown; null when nothing was ever chosen.</summary>
    public string? Hidden => _values.GetValueOrDefault(HideKey);

    /// <summary>The types the grid hides, the same way.</summary>
    public string? HiddenLog => _values.GetValueOrDefault(HideLogKey);

    /// <summary>The level chosen ("all" for none); null when nothing was ever chosen.</summary>
    public string? Level => _values.GetValueOrDefault(LevelKey);

    /// <summary>The Activity list's Autorefresh: on until the user switches it off.</summary>
    public bool FollowCli => _values.GetValueOrDefault(FollowCliKey) != "0";

    /// <summary>The grid's Autorefresh: on until the user switches it off.</summary>
    public bool FollowLog => _values.GetValueOrDefault(FollowLogKey) != "0";

    /// <summary>The divider's place — the grid's height in pixels, what is left going to the strip; null until the divider was dragged.</summary>
    public int? Grid => int.TryParse(_values.GetValueOrDefault(GridKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var px) ? px : null;

    public void SetHidden(string hidden) => Set(HideKey, hidden);

    public void SetHiddenLog(string hidden) => Set(HideLogKey, hidden);

    public void SetLevel(string level) => Set(LevelKey, level);

    public void SetFollowCli(bool follow) => Set(FollowCliKey, follow ? "1" : "0");

    public void SetFollowLog(bool follow) => Set(FollowLogKey, follow ? "1" : "0");

    public void SetGrid(int px) => Set(GridKey, px.ToString(CultureInfo.InvariantCulture));

    private void Set(string key, string value)
    {
        if (_values.TryGetValue(key, out var current) && current == value)
        {
            return;
        }

        _values[key] = value;
        _ = SaveAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var stored = await js.InvokeAsync<string?>("wslcAgent.logs");
            foreach (var pair in (stored ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=');
                if (separator > 0)
                {
                    _values[pair[..separator]] = pair[(separator + 1)..];
                }
            }
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
            await js.InvokeAsync<string?>("wslcAgent.logs", string.Join(';', _values.Select(pair => $"{pair.Key}={pair.Value}")));
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Storage refused or the web view is gone: the choice still holds for this run.
        }
    }
}
