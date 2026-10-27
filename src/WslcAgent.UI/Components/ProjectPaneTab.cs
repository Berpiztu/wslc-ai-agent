using Microsoft.AspNetCore.Components;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.UI.Components;

/// <summary>The tabs a project's window of two panes can hold, in the order it shows them.</summary>
public enum ProjectTab
{
    /// <summary>What a container's card says of it; All, every container's.</summary>
    Information,

    /// <summary>A container's logs, or its work's while the agent carries it out; All, the project's as one.</summary>
    Logs,

    /// <summary>A container's Update image.</summary>
    UpdateImage,

    /// <summary>Zaindari: a container's vulnerabilities, what to accept, its patch.</summary>
    Zaindari,
}

/// <summary>
/// One tab of a project's window of two panes (<c>ProjectPanesDialog</c>):
/// Information, Logs, Update image, Zaindari. It is given the container chosen
/// (null for All), the project's containers, the window's verb and a way to
/// read the containers again; it draws the pane, and gives the window's title
/// line its verb, if it has one there. The window repaints the title line when
/// <see cref="Repaint"/> is called.
/// </summary>
public abstract class ProjectPaneTab : ComponentBase
{
    /// <summary>The project's name.</summary>
    [Parameter, EditorRequired] public string Project { get; set; } = "";

    /// <summary>The container chosen; null for All.</summary>
    [Parameter] public ContainerSummary? Chosen { get; set; }

    /// <summary>The project's containers, as the menu has them.</summary>
    [Parameter] public IReadOnlyList<ContainerSummary> Members { get; set; } = [];

    /// <summary>The window's verb: one at a time, its veil over the pane of the container it runs on.</summary>
    [Parameter, EditorRequired] public PaneVerb Verb { get; set; } = default!;

    /// <summary>Reads the project's containers again, after a verb: the menu then says what the agent is doing.</summary>
    [Parameter, EditorRequired] public Func<Task> ReadAgain { get; set; } = default!;

    /// <summary>Repaints the window, its title line included.</summary>
    [Parameter, EditorRequired] public Action Repaint { get; set; } = default!;

    /// <summary>The tab is one of several (the Project window), not the pane itself: moving between its tabs is the way back, and a tab needs no Cancel of its own.</summary>
    [Parameter] public bool AmongOthers { get; set; }

    /// <summary>The tab's verb on the title line, before Close (Update); a tab with steps keeps its verbs in a bar of its own (Zaindari).</summary>
    public virtual RenderFragment? Verbs => null;

    /// <summary>What the veil says while the tab's verb runs.</summary>
    public virtual string BusyText => "";

    /// <summary>The verb runs on the container shown: its button says so; on another one it only waits.</summary>
    protected bool RunningHere => Verb.Busy && Verb.For == Chosen?.Id;

    /// <summary>Drawn the first time, the tab is there for the window to take its keys and its verb: the title line is drawn again.</summary>
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            Repaint();
        }
    }
}
