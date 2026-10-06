using System.Text.Json.Serialization;

namespace Berpiztu.Dashboard.Model;

/// <summary>
/// A group of objects: a frame on the canvas that keeps them together, and
/// nothing more. It works as a card: taken whole, moved whole, sized by its corner, its
/// objects taken one by one inside it. Out of design it is drawn as a card.
/// Its objects say which group they are in (<see cref="ObjectInstance.Group"/>)
/// and stand inside its frame.
/// </summary>
/// <param name="Id">The group's own, given when it is made and never changed.</param>
/// <param name="Background">
/// The colour its card is filled with; null is its default,
/// <see cref="ThemeColors.GroupBackground"/>. <see cref="ThemeColor.Inherited"/>
/// takes the page's.
/// </param>
/// <param name="Foreground">
/// The colour of its text; null is its default,
/// <see cref="ThemeColors.GroupForeground"/>. Its objects set to
/// <see cref="ThemeColor.Inherited"/> take it.
/// </param>
/// <param name="Template">
/// The card made already it was dropped as (its template's type), whose size
/// in another view its frame takes there; null for a group the user made.
/// </param>
/// <param name="Elevation">
/// How far its card stands off the page, 0 to <see cref="MaxElevation"/> as
/// MudBlazor's cards take it, as the Containers screen's cards have it; null is the application's own for its
/// cards, which the designer is given.
/// </param>
/// <param name="Anchor">
/// Which of the view's edges its card keeps to as a fluid view widens with the
/// screen, as its objects keep to the card's
/// (<see cref="HorizontalAnchor"/>); null is <see cref="HorizontalAnchor.Scale"/>.
/// </param>
/// <param name="Border">
/// The colour of its card's line out of design (the tertiary under the
/// pointer); <see cref="ThemeColor.Transparent"/> is none, null its default,
/// <see cref="ThemeColors.CardBorder"/>.
/// </param>
public sealed record DashboardGroup(string Id, int X, int Y, int W, int H, ThemeColor? Background = null, ThemeColor? Foreground = null,
    string? Template = null, int? Elevation = null, HorizontalAnchor? Anchor = null, ThemeColor? Border = null)
{
    /// <summary>The highest elevation a card takes, MudBlazor's highest shadow but one, as the application's settings allow.</summary>
    public const int MaxElevation = 24;

    /// <summary>The cells its frame covers; worked out, never stored.</summary>
    [JsonIgnore]
    public CellBox Box => new(X, Y, W, H);
}
