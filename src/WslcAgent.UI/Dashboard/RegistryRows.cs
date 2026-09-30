namespace WslcAgent.UI.Dashboard;

/// <summary>
/// One of the agent's resource lists (containers, images, volumes, networks),
/// read for the dashboard's objects of that family: its rows, each numbered by
/// the agent's resource registry, which no rename and no recreate changes, so
/// an object finds its resource by that number.
/// </summary>
/// <typeparam name="TList">What the list's route answers.</typeparam>
/// <typeparam name="TRow">One resource of it.</typeparam>
public abstract class RegistryRows<TList, TRow>(TimeSpan every) : SharedRead<TList>(every)
    where TList : class
    where TRow : class
{
    /// <summary>The rows of the last read that answered; null until one has.</summary>
    public IReadOnlyList<TRow>? Rows => Value is { } list ? RowsOf(list) : null;

    /// <summary>The row of this registry uid; null when it is not known, or not read yet.</summary>
    public TRow? Find(int uid) => Rows?.FirstOrDefault(row => UidOf(row) == uid);

    /// <summary>
    /// Whether the resource of this uid is gone for certain: a read answered
    /// and it was not in it. Not read yet, or a read that failed, says nothing.
    /// </summary>
    public bool Gone(int uid) => Rows is not null && Find(uid) is null;

    /// <summary>The resource's registry uid; 0 for one the registry does not number (a dangling image).</summary>
    public abstract int UidOf(TRow row);

    /// <summary>The name the resource reads by, in the properties window's list.</summary>
    public abstract string NameOf(TRow row);

    protected abstract IReadOnlyList<TRow> RowsOf(TList list);
}
