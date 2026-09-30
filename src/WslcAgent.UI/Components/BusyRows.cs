namespace WslcAgent.UI.Components;

/// <summary>
/// Which rows have a verb in progress, by the row's key (container id, image
/// reference, volume or network name). The actions component of a row marks
/// it; the row's state dot and its card read it and show the work in place,
/// without a bar that moves anything. Several rows may be busy at once.
/// </summary>
public sealed class BusyRows
{
    private readonly HashSet<string> _keys = [];

    public event Action? Changed;

    public bool IsBusy(string? key) => key is not null && _keys.Contains(key);

    public void Begin(string key)
    {
        if (_keys.Add(key))
        {
            Changed?.Invoke();
        }
    }

    public void End(string key)
    {
        if (_keys.Remove(key))
        {
            Changed?.Invoke();
        }
    }
}
