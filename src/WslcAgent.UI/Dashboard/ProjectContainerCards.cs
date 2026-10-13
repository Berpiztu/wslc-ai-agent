using System.Globalization;
using Berpiztu.Dashboard.Catalogue;
using Berpiztu.Dashboard.Fields;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;
using WslcAgent.UI.Dashboard.Cards;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What a project's card holds on the dashboard beside its own pieces: its
/// containers, each laid out inside it as a container's card is, so the
/// project is one card with its containers in it, as the four charts are
/// one card (<see cref="ICardCompanions"/>). Chosen for a card, a project
/// comes with every container it has; the card's properties list them, each
/// with its tick, and one made since is there unticked, for the user to
/// show or not. Each reads its container by its registry uid, as a
/// container's own card does. A container wanted on its own is its own
/// card, from the toolbox.
/// <para>
/// The card's line is the project's, the one that runs round its cards in
/// the Containers list (<see cref="ProjectRow.Line"/>): the success colour
/// with every container running, the primary with none, stripes of the two
/// with some, or while something is being done to it.
/// </para>
/// </summary>
public sealed class ProjectContainerCards : ICardCompanions, IDisposable
{
    private readonly ContainerRows _rows;
    private readonly ProjectVerbs _verbs;
    private readonly BusyRows _busy;

    public ProjectContainerCards(ContainerRows rows, ProjectVerbs verbs, BusyRows busy)
    {
        (_rows, _verbs, _busy) = (rows, verbs, busy);
        _rows.Changed += OnChanged;
        _verbs.Changed += OnChanged;
        _busy.Changed += OnChanged;
    }

    public event Action? Changed;

    public string Template => WslcCards.Project.Type;

    public string Label => "Containers";

    public string Noun => "container";

    public IDashboardTemplate Card => WslcCards.Container;

    /// <summary>Two to a row, as the charts' card has its four: the project and its first container, then two containers a row.</summary>
    public int PerRow => 2;

    /// <summary>The project's containers, in the order the agent first knew them, read again each time; one the registry does not number is left out.</summary>
    public async Task<IReadOnlyList<FieldOption<string>>> ListAsync(string source)
    {
        if (ProjectSource.Name(source) is not { } project)
        {
            return [];
        }

        await _rows.RefreshAsync();
        return [.. Members(project)
            .Where(container => container.Uid > 0)
            .OrderBy(container => container.Uid)
            .Select(container => new FieldOption<string>(container.Uid.ToString(CultureInfo.InvariantCulture), container.Name))];
    }

    /// <summary>The project's line; nothing until its containers are read, and the card keeps its own.</summary>
    public string LineOf(string source)
    {
        if (ProjectSource.Name(source) is not { } project || Members(project) is not { Count: > 0 } members)
        {
            return "";
        }

        var working = ProjectRow.Working(project, members, _verbs, _busy);
        return $"wslc-bracket {ProjectRow.Line(members, working)}";
    }

    public void Dispose()
    {
        _rows.Changed -= OnChanged;
        _verbs.Changed -= OnChanged;
        _busy.Changed -= OnChanged;
    }

    private List<ContainerSummary> Members(string project) =>
        [.. (_rows.Rows ?? []).Where(container => container.Project == project)];

    private void OnChanged() => Changed?.Invoke();
}
