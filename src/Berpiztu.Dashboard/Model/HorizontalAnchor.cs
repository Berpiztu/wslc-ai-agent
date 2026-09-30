namespace Berpiztu.Dashboard.Model;

/// <summary>
/// Which of its card's edges an object keeps to as the card widens with the
/// screen out of design, as the old Visual Basic's and Windows Forms' Anchor:
/// so a card widened by a fluid view behaves as the list pages' card does —
/// its words taking the room, its dials together at its right edge.
/// </summary>
public enum HorizontalAnchor
{
    /// <summary>To neither: it spreads across the card in proportion, as every cell does.</summary>
    Scale,

    /// <summary>To the left edge: its size and its distance to it kept.</summary>
    Left,

    /// <summary>To the right edge: its size and its distance to it kept.</summary>
    Right,

    /// <summary>To both: its distance to each kept, and it widens with the card.</summary>
    Both,
}
