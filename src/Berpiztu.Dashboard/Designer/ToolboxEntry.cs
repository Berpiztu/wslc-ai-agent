using Berpiztu.Dashboard.Catalogue;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// One thing the toolbox offers: an object of a kind, or a card made already
/// (a template), in three steps: its group —
/// Cards, or Objects for every kind of object —, its subgroup within it (User,
/// System), and, an object, its section within that (the kind's own group:
/// Container, Host dials…); each step a heading indented under the one above.
/// </summary>
public sealed record ToolboxEntry(string Label, string Icon, string Group, string Subgroup, string Section, ObjectDescriptor? Kind, IDashboardTemplate? Template)
{
    /// <summary>The toolbox's group every kind of object stands in, beside the cards'.</summary>
    public const string Objects = "Objects";

    public static ToolboxEntry Of(ObjectDescriptor kind) =>
        new(kind.Label, kind.About.Icon, Objects, kind.About.Subgroup, kind.About.Group, kind, null);

    public static ToolboxEntry Of(IDashboardTemplate template) =>
        new(template.Label, template.Icon, template.Group, template.Subgroup, "", null, template);

    /// <summary>
    /// What the toolbox offers, in its order: by group; then by subgroup in the
    /// order the cards give theirs (User before System), any other after them
    /// and one with none last; then by section, by name; then by name. A piece
    /// that lives only in its card comes with the card, never alone.
    /// </summary>
    public static IReadOnlyList<ToolboxEntry> Ordered(IEnumerable<ObjectDescriptor> kinds, IEnumerable<IDashboardTemplate> templates)
    {
        var entries = templates.Select(template => Of(template)).Concat(kinds.Where(kind => !kind.About.CardOnly).Select(kind => Of(kind))).ToList();
        var subgroups = entries.Select(entry => entry.Subgroup).Where(subgroup => subgroup.Length > 0).Distinct().ToList();
        return [.. entries
            .OrderBy(entry => entry.Group, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Subgroup.Length == 0 ? int.MaxValue : subgroups.IndexOf(entry.Subgroup))
            .ThenBy(entry => entry.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Label, StringComparer.CurrentCultureIgnoreCase)];
    }
}
