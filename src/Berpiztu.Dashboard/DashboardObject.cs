using Berpiztu.Dashboard.Catalogue;
using Berpiztu.Dashboard.Designer;
using Berpiztu.Dashboard.Model;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Berpiztu.Dashboard;

/// <summary>
/// What every object on the dashboard inherits. An object draws only what it shows: the canvas draws the
/// rest around it — its cells, its alignment, the selection and the handle in
/// design, and the empty place while its source is not chosen — so every
/// object gets them the same, and none draws them twice.
/// </summary>
/// <remarks>
/// An object is a component in a folder of its own that inherits this and
/// carries a <see cref="Catalogue.DashboardObjectAttribute"/>: the catalogue
/// finds it by that, and the toolbox offers it.
/// </remarks>
public abstract class DashboardObject : ComponentBase
{
    /// <summary>The object as the dashboard holds it: its source, its cells, its type size and alignment.</summary>
    [Parameter, EditorRequired] public ObjectInstance Instance { get; set; } = default!;

    /// <summary>The dashboard is being designed: what the object shows is inert, and the hand moves it.</summary>
    [Parameter] public bool Designing { get; set; }

    [CascadingParameter] private DashboardDocument? Document { get; set; }

    /// <summary>The canvas was drawn again with the object as it was: the render that asks for is skipped.</summary>
    private bool _asItWas;

    /// <summary>
    /// The canvas draws itself again for its own reasons — a drag's ghost
    /// moving a cell, a frame being carried, the selection — and hands every
    /// object its instance again. An object is drawn again only when it is
    /// another instance or the mode changed; what it reads still draws it
    /// when it changes (otherwise the Charts card
    /// carried over a dashboard held the page for seconds, every chart drawn
    /// again at each step of the pointer).
    /// </summary>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        _asItWas = Instance is { } drawn
            && parameters.TryGetValue<ObjectInstance>(nameof(Instance), out var given) && given == drawn
            && parameters.GetValueOrDefault(nameof(Designing), false) == Designing;
        return base.SetParametersAsync(parameters);
    }

    protected override bool ShouldRender()
    {
        var changed = !_asItWas;
        _asItWas = false;
        return changed;
    }

    [Inject] private ObjectCatalogue Catalogue { get; set; } = default!;

    /// <summary>What it reads: its own source, or its kind's default until one is chosen.</summary>
    protected string? Source => Catalogue.Find(Instance.Type)?.SourceOf(Instance) ?? Instance.Source;

    /// <summary>Where what it shows stands across its cells: its own alignment, or its kind's until one is chosen.</summary>
    protected HorizontalAlign Horizontal => Catalogue.Find(Instance.Type)?.HorizontalOf(Instance) ?? Instance.Horizontal ?? HorizontalAlign.Center;

    /// <summary>Where what it shows stands down its cells: its own alignment, or its kind's until one is chosen.</summary>
    protected VerticalAlign Vertical => Catalogue.Find(Instance.Type)?.VerticalOf(Instance) ?? Instance.Vertical ?? VerticalAlign.Middle;

    /// <summary>The type size as MudBlazor sizes its components, for an object drawn by one that takes a <see cref="Size"/>.</summary>
    protected Size ComponentSize => Instance.Size switch
    {
        TypeSize.Small => Size.Small,
        TypeSize.Large => Size.Large,
        _ => Size.Medium,
    };

    /// <summary>Whether this part is drawn: false once the user hid it (<see cref="Catalogue.DashboardObjectPartAttribute.Optional"/>).</summary>
    protected bool Shows(string part) => Instance.PartOf(part).Hidden != true;

    /// <summary>
    /// It opens something fuller when it is tapped again once selected, or by
    /// the page's Open verb (a chart its full
    /// chart, a count its page); false for one that opens nothing.
    /// </summary>
    public virtual bool Opens => false;

    /// <summary>What it opens; nothing for one that <see cref="Opens"/> not.</summary>
    public virtual Task OpenAsync() => Task.CompletedTask;

    /// <summary>
    /// The object's source is not on this agent (its container was deleted,
    /// or the dashboard came from another machine): it stays in the
    /// dashboard, shown in design for another source to be chosen and left
    /// out of the view (<see cref="DashboardDocument.Lose"/>). Called by the
    /// object once what it reads says so for certain — a read that failed
    /// says nothing either way.
    /// </summary>
    protected Task SourceGoneAsync()
    {
        Document?.Lose(Instance);
        return Task.CompletedTask;
    }
}
