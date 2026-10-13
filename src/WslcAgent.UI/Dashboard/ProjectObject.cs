using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.UI.Components;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// An object of type <c>project</c>: its source is a Compose project, chosen
/// in the properties window by its name, and it reads the container list,
/// which is where a project is (<see cref="ProjectSourceFamily"/>). It is
/// drawn again with each read, and once a read says the project has no
/// container left it is kept for another to be chosen, as an object whose
/// container was removed is: shown so in design, left out of the view.
/// </summary>
public abstract class ProjectObject : WslcObject
{
    [Inject] protected ContainerRows Containers { get; set; } = default!;

    [Inject] protected ProjectVerbs Verbs { get; set; } = default!;

    [Inject] private BusyRows Busy { get; set; } = default!;

    /// <summary>The project's name; null while no project is chosen.</summary>
    protected string? Project => ProjectSource.Name(Instance.Source);

    /// <summary>The project's containers, in the order the agent first knew them; none until the list is read, or once it has none.</summary>
    protected IReadOnlyList<ContainerSummary> Members =>
        Project is { } name && Containers.Rows is { } rows
            ? [.. rows.Where(container => container.Project == name).OrderBy(container => container.Uid)]
            : [];

    /// <summary>The project as one row (<see cref="ProjectRow"/>), what its card says of it; null until its containers are read.</summary>
    protected ContainerSummary? Head => Project is { } name && Members is { Count: > 0 } members ? ProjectRow.Of(name, members) : null;

    /// <summary>Its containers by name: what a down says it removes.</summary>
    protected IReadOnlyList<string> MemberNames => [.. Members.Select(member => member.Name)];

    /// <summary>
    /// The class of a piece that carries the card's tone (its header, its
    /// rows of verbs) while something is being done to the project: the tone
    /// goes from the stopped one to the running one and back, as the
    /// project's card does in the Containers list. Empty otherwise.
    /// </summary>
    protected string WaitingClass =>
        Project is { } name && ProjectRow.Working(name, Members, Verbs, Busy) ? "wslc-bracket-waiting" : "";

    protected override void OnInitialized()
    {
        Follow(Containers, OnRead);
        // A verb and a row's mark are this client's, not the agent's: they move between two reads of the list.
        Hear(h => Verbs.Changed += h, h => Verbs.Changed -= h);
        Hear(h => Busy.Changed += h, h => Busy.Changed -= h);
    }

    private void OnRead() => _ = InvokeAsync(() =>
    {
        // A read that answered and found none of its containers: the project is gone for certain.
        if (Project is not null && Containers.Rows is not null && Members.Count == 0)
        {
            return SourceGoneAsync();
        }

        StateHasChanged();
        return Task.CompletedTask;
    });
}
