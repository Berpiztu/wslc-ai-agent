namespace WslcAgent.UI.Dashboard;

/// <summary>
/// A shared read that keeps its last samples as well as its last value: the
/// history the dashboard's charts draw, two minutes at the refresh interval.
/// It belongs to the read, not to a
/// chart, so every chart of a measure draws the same history, and a chart
/// dropped later draws what was already sampled.
/// </summary>
public abstract class SampledRead<T>(TimeSpan every) : SharedRead<T>(every) where T : class
{
    /// <summary>The chart history: two minutes at the default refresh.</summary>
    private const int MaxSamples = 120;

    private readonly List<T> _history = [];

    /// <summary>The samples that answered with a measure, oldest first.</summary>
    public IReadOnlyList<T> History => _history;

    /// <summary>A sample that answered without its measure (the CLI's stats failed), which the history leaves out.</summary>
    protected abstract bool Failed(T sample);

    protected override void Keep(T value)
    {
        if (Failed(value))
        {
            return;
        }

        _history.Add(value);
        if (_history.Count > MaxSamples)
        {
            _history.RemoveAt(0);
        }
    }
}
