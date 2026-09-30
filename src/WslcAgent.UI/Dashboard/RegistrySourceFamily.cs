using System.Globalization;
using Berpiztu.Dashboard.Fields;
using Berpiztu.Dashboard.Sources;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// One of the agent's resource families as a family objects take their
/// source from: each row's value is the
/// resource's uid in the agent's resource registry, which no rename and no
/// recreate changes, and it reads by the resource's name. The properties
/// window drops them as a list under the field.
/// </summary>
public abstract class RegistrySourceFamily<TList, TRow>(RegistryRows<TList, TRow> rows) : ISourceFamily
    where TList : class
    where TRow : class
{
    public abstract string Key { get; }

    public abstract string Label { get; }

    public abstract string Noun { get; }

    /// <summary>Read again each time the list opens, so one just made is there; by name. One the registry does not number is left out.</summary>
    public async Task<IReadOnlyList<FieldOption<string>>> ListAsync()
    {
        await rows.RefreshAsync();
        return [.. (rows.Rows ?? [])
            .Where(row => rows.UidOf(row) > 0)
            .OrderBy(rows.NameOf, StringComparer.CurrentCultureIgnoreCase)
            .Select(row => new FieldOption<string>(rows.UidOf(row).ToString(CultureInfo.InvariantCulture), rows.NameOf(row)))];
    }
}

/// <summary>What a resource family's source value holds.</summary>
public static class RegistrySource
{
    /// <summary>The registry uid a source value holds; null for none, or for one that is not a uid.</summary>
    public static int? Uid(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) && uid > 0 ? uid : null;
}
