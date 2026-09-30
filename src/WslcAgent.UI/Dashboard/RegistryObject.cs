namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What every object of a resource family shares: its source is one resource,
/// chosen in the properties window by its registry uid; it reads the list the
/// family's objects share (<see cref="RegistryRows{TList, TRow}"/>), is drawn
/// again with each read, and leaves the dashboard once a read says its
/// resource is gone.
/// </summary>
public abstract class RegistryObject<TList, TRow> : WslcObject
    where TList : class
    where TRow : class
{
    /// <summary>The family's list, which the object follows.</summary>
    protected abstract RegistryRows<TList, TRow> Rows { get; }

    /// <summary>The resource's row; null until the list is read, or while its resource is not in it.</summary>
    protected TRow? Resource => Uid is { } uid ? Rows.Find(uid) : null;

    /// <summary>The whole of the last read, for what a row is a share of (every container's reads and writes).</summary>
    protected TList? List => Rows.Value;

    private int? Uid => RegistrySource.Uid(Instance.Source);

    protected override void OnInitialized() => Follow(Rows, OnRead);

    private void OnRead() => _ = InvokeAsync(() =>
    {
        if (Uid is { } uid && Rows.Gone(uid))
        {
            return SourceGoneAsync();
        }

        StateHasChanged();
        return Task.CompletedTask;
    });
}
