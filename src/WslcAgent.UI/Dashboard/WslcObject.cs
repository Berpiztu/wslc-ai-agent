using Berpiztu.Dashboard;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// What every WSLC object on the dashboard adds to the SDK's base: its type
/// size as the ladder the UI steps a card's parts by (<c>wslc-type-small</c>,
/// <c>-medium</c>, <c>-large</c>), so a dial, a header or a row of verbs of
/// the same size draws the same wherever it stands, and Small and Medium
/// differ.
/// </summary>
public abstract class WslcObject : DashboardObject, IDisposable
{
    private readonly List<Action> _letGo = [];

    /// <summary>
    /// Follows one of the agent's shared reads while the object is on the
    /// dashboard: it keeps being read, and each read draws the object again,
    /// or runs <paramref name="onRead"/> instead.
    /// </summary>
    /// <returns>What lets go of it before the object leaves, for a read the object stops showing (a chart given another subject).</returns>
    protected IDisposable Follow<T>(SharedRead<T> read, Action? onRead = null) where T : class
    {
        var handler = onRead ?? Redraw;
        read.Changed += handler;
        var following = read.Follow();
        var done = false;
        void LetGo()
        {
            if (!done)
            {
                done = true;
                read.Changed -= handler;
                following.Dispose();
            }
        }

        _letGo.Add(LetGo);
        return new Letting(LetGo);
    }

    /// <summary>A following let go of on its own.</summary>
    private sealed class Letting(Action letGo) : IDisposable
    {
        public void Dispose() => letGo();
    }

    /// <summary>
    /// Hears a notice of something the object shows that is not a shared read
    /// (the session, the client's update, this client's queue), drawing the
    /// object again at each, for as long as it is on the dashboard.
    /// </summary>
    /// <param name="add">Subscribes a handler: <c>h => Session.Changed += h</c>.</param>
    /// <param name="remove">Unsubscribes it: <c>h => Session.Changed -= h</c>.</param>
    protected void Hear(Action<Action> add, Action<Action> remove)
    {
        Action handler = Redraw;
        add(handler);
        _letGo.Add(() => remove(handler));
    }

    /// <summary>
    /// What a reading says under its value: Loading…
    /// until it is read, Unavailable when the read failed, or what it means.
    /// </summary>
    protected static string ReadingHint(bool read, bool failed, string meaning) =>
        !read ? "Loading…" : failed ? "Unavailable" : meaning;

    /// <summary>Drawn again from the UI's thread, for what the object hears of outside a render.</summary>
    protected void Redraw() => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        _letGo.ForEach(letGo => letGo());
        _letGo.Clear();
    }

    /// <summary>
    /// The class of the type size, for the element the object draws around
    /// what it shows: the type ladder, and <c>wslc-dash-type</c>, where the
    /// dashboard steps in one proportion what that ladder does not (a dial's
    /// ring, a button's glyph).
    /// </summary>
    protected string TypeClass => $"wslc-dash-type wslc-type-{Instance.Size.ToString().ToLowerInvariant()}";

    /// <summary>The classes of a reading (a label, its value, its hint), whose three texts the type size scales.</summary>
    protected string ReadingClass => $"{TypeClass} wslc-dash-reading";

    /// <summary>The classes of a System card reading (a label over what it names), whose label and value the type size scales.</summary>
    protected string SummaryClass => $"{TypeClass} wslc-dash-summary";

    /// <summary>
    /// The classes of an object drawn as a section of a list's card (a header,
    /// the details, a row of verbs), which fills its cells: its type size
    /// steps it; its ground is the object's Background, not the card's tone
    /// (Transparent drew the footer's blue); and what it holds stands where
    /// its alignment says, across and down — its kind's, where it stands on
    /// the card, until one is chosen. The canvas cannot place what an
    /// object that fills its cells holds, so the section does
    /// (<c>wslc-dash-h-*</c>, <c>wslc-dash-v-*</c>).
    /// </summary>
    /// <param name="part">The part of the card it is, for the part's own steps (<c>wslc-card-part-summary</c>).</param>
    protected string SectionClass(string part = "") =>
        $"wslc-card-section wslc-dash-section {part} {TypeClass} {HorizontalClass}"
        + $" wslc-dash-v-{Vertical.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Where what it holds stands across (<c>wslc-dash-h-left</c>, <c>-center</c>,
    /// <c>-right</c>), for what spreads across the whole width and places
    /// its items itself — a section, a legend whose entries wrap — which
    /// the canvas's alignment cannot move.
    /// </summary>
    protected string HorizontalClass => $"wslc-dash-h-{Horizontal.ToString().ToLowerInvariant()}";
}
