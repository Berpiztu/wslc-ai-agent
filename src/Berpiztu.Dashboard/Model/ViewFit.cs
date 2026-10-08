namespace Berpiztu.Dashboard.Model;

/// <summary>
/// How a view is fitted to a screen of another aspect, out of design, as a
/// television offers it, chosen on each device beside the dashboard's zoom.
/// </summary>
public enum ViewFit
{
    /// <summary>The whole view, as large as it goes with its aspect kept; the room left over shared at both sides.</summary>
    Fit,

    /// <summary>The screen filled, the aspect kept: the view as large as fills both sides, what goes past the screen scrolled to.</summary>
    Fill,

    /// <summary>The screen's width filled, larger or smaller, the aspect kept: what goes past its bottom scrolled to.</summary>
    Width,

    /// <summary>The screen filled both ways, the aspect given up: the cells stretched to rectangles, what they show at the smaller of the two sizes.</summary>
    Stretch,

    /// <summary>
    /// Fluid width, as the Containers screen's cards: nothing zoomed, what the cards show at its own size; the view's
    /// columns share the screen's whole width, its rows keep their height, and
    /// what goes past the bottom is scrolled to.
    /// </summary>
    Fluid,

    /// <summary>
    /// One to one: the view at its own size, every cell as large as in
    /// design, from the screen's top-left corner as in design; nothing zoomed,
    /// nothing centred, what goes past the screen scrolled to. Leaving design
    /// or coming into it, everything stays where it was seen (the owner's
    /// request of 7 October 2026: nothing done by itself).
    /// </summary>
    Actual,
}
