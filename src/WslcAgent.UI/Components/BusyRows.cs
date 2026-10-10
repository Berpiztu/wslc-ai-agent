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

    /// <summary>The verb each busy row is in (start, stop, restart, remove), when whoever marked it said: a stop is drawn running down, where the rest run up.</summary>
    private readonly Dictionary<string, string> _doing = new(StringComparer.Ordinal);

    public event Action? Changed;

    public bool IsBusy(string? key) => key is not null && _verbs.ContainsKey(key);

    /// <summary>The verb running on the row; empty when none is, or it was not said.</summary>
    public string VerbOf(string? key) => key is not null ? _doing.GetValueOrDefault(key, "") : "";

    /// <summary>What is running on the row brings it down (a stop, a kill): its bar and its ring move the other way round.</summary>
    public bool IsStopping(string? key) => VerbOf(key) is "stop" or "kill";

    public void Begin(string key, string verb = "")
    {
        _verbs[key] = _verbs.GetValueOrDefault(key) + 1;

        // A second mark that names no verb leaves the first one's standing.
        if (verb.Length > 0 || !_doing.ContainsKey(key))
        {
            _doing[key] = verb;
        }

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
        _doing.Remove(key);
        Changed?.Invoke();
    }
}
