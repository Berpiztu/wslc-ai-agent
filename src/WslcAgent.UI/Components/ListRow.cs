namespace WslcAgent.UI.Components;

/// <summary>
/// A list page's row as the grid knows it: the item, and the key that makes it
/// the same row after a refresh. The grid keys its rows (<c>@key</c>) and
/// matches its selection by the row's equality, so equality is the key alone:
/// a polled row whose CPU or state changed stays the same row, keeps its DOM,
/// its tick and an open menu, and only its cells are updated. Compared by
/// value instead, a row holding a list never equals itself after a poll, and
/// every row was drawn anew every five seconds.
/// </summary>
public sealed class ListRow<TItem>(string key, TItem value) : IEquatable<ListRow<TItem>>
{
    public string Key { get; } = key;

    public TItem Value { get; } = value;

    public bool Equals(ListRow<TItem>? other) => other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ListRow<TItem>);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);
}
