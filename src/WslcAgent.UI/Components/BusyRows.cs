namespace WslcAgent.UI.Components;

/// <summary>
/// Which rows have a verb in progress, by the row's key (container id, image
/// reference, volume or network name). The actions component of a row marks
/// it; the row's state dot and its card read it and show the work in place,
/// without a bar that moves anything. Several rows may be busy at once, and
/// one row for several verbs at once (a project's, while two of its containers
/// are being stopped): it is busy until the last of them ends.
/// </summary>
public sealed class BusyRows
{
    private readonly Dictionary<string, int> _verbs = new(StringComparer.Ordinal);

    public event Action? Changed;

    public bool IsBusy(string? key) => key is not null && _verbs.ContainsKey(key);

    public void Begin(string key)
    {
        _verbs[key] = _verbs.GetValueOrDefault(key) + 1;
        if (_verbs[key] == 1)
        {
            Changed?.Invoke();
        }
    }

    public void End(string key)
    {
        if (!_verbs.TryGetValue(key, out var verbs))
        {
            return;
        }

        if (verbs > 1)
        {
            _verbs[key] = verbs - 1;
            return;
        }

        _verbs.Remove(key);
        Changed?.Invoke();
    }
}
