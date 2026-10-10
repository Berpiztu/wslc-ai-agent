namespace WslcAgent.UI.Components;

/// <summary>
/// How long a start or a stop is shown working, at the least. Starting a
/// container returns as soon as it has been launched, a third of a second, so
/// what shows the work (a ring, a pulsing dot, a project's progress) turned
/// for an instant and was gone before the press could be seen to do anything.
/// It is shown for at least this long; stopping waits for the container to go
/// down and is usually longer on its own.
/// </summary>
public static class VerbSpin
{
    private static readonly TimeSpan Minimum = TimeSpan.FromSeconds(2);

    /// <summary>The work, and no sooner over than the minimum; what the work throws is thrown once both are through.</summary>
    public static Task AtLeast(Task work, CancellationToken cancellationToken = default) =>
        Task.WhenAll(work, Task.Delay(Minimum, cancellationToken));
}
