using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// A view laid out fluid (<see cref="ViewFit.Fluid"/>), in pixels from the
/// view's first cell: what stands side by side in the design stays side by
/// side while it fits the width it is given, and what does not fit goes down
/// to a line of its own. Nothing is decided here that the design does not
/// say: each card and object is placed by its own anchor, in what holds it
/// (the view, or its card), and the rules are the same at every level.
/// <list type="bullet">
/// <item>What holds things (the view, a card) has them in bands: those whose
/// rows meet stand in one band, side by side, in the design's order.</item>
/// <item>A band whose pieces, each placed by its anchor, all stand inside
/// their holder at no less than their own size (Scale at any) and none on
/// another is placed so, as fluid always placed it:
/// Scale spread in proportion, Left and Right at its own size to that edge,
/// Both widened or narrowed by what the holder gained or lost.</item>
/// <item>A band that does not (a piece past the holder's edge, or two on
/// each other) is broken into lines, each taking what fits at its own size
/// (the first always at least one): Left at its own size from the left,
/// Right at its own size to the right, Both and Scale sharing what is left
/// over, in proportion to their size, and taking the whole line when alone
/// on it.</item>
/// <item>A card grows down by what its own lines took, the band it stands in
/// with it, and what stands under it, in its holder and in the view, comes
/// down as much. Nothing grows across.</item>
/// </list>
/// It changes how the view is drawn, never the design.
/// </summary>
internal sealed class FluidFlow
{
    /// <summary>Where a card's frame or an object stands, in pixels from the view's first cell, before what it keeps from its holder's line.</summary>
    public readonly record struct Spot(double Left, double Top, double Width, double Height);

    private readonly DashboardLayout _layout;
    private readonly double _cell;
    private readonly Func<ObjectInstance, HorizontalAnchor> _anchorOf;
    private readonly Func<CellBox, bool> _seen;
    private readonly Dictionary<string, Spot> _spots = new(StringComparer.Ordinal);

    /// <param name="layout">The view as designed, its cells counted from the view's first one.</param>
    /// <param name="columns">The view's columns, and <paramref name="rows"/> its rows.</param>
    /// <param name="cell">A design cell's side, in pixels: what a piece measures at its own size.</param>
    /// <param name="room">The view's width on the screen, in pixels.</param>
    /// <param name="anchorOf">An object's anchor, its own or its kind's.</param>
    /// <param name="seen">Whether an object or a card is drawn at all: one wholly outside the view takes no room.</param>
    private FluidFlow(DashboardLayout layout, int columns, int rows, double cell, double room, Func<ObjectInstance, HorizontalAnchor> anchorOf, Func<CellBox, bool> seen)
    {
        _layout = layout;
        _cell = cell;
        _anchorOf = anchorOf;
        _seen = seen;
        Height = Lay(Children(null), 0, room, 0, columns, 0, 0, rows);
    }

    /// <summary>How tall the view stands, its lines and all, in pixels.</summary>
    public double Height { get; }

    /// <summary>The view laid out fluid.</summary>
    public static FluidFlow Of(DashboardLayout layout, int columns, int rows, double cell, double room, Func<ObjectInstance, HorizontalAnchor> anchorOf, Func<CellBox, bool> seen) =>
        new(layout, columns, rows, cell, room, anchorOf, seen);

    /// <summary>Where a card or an object stands; null for one not laid out (not drawn).</summary>
    public Spot? SpotOf(string id) => _spots.TryGetValue(id, out var spot) ? spot : null;

    /// <summary>A card or an object as the flow sees it: its id, its cells, its anchor, and whether it is a card holding others.</summary>
    private sealed record Piece(string Id, CellBox Box, HorizontalAnchor Anchor, bool Card);

    /// <summary>
    /// What stands directly in a card (its objects and the cards inside it),
    /// or on the page for null. On the page only what the view shows takes
    /// room; in a card, all of it, inside the card's frame or left outside it,
    /// since the card goes where it is laid out and takes its own with it.
    /// </summary>
    private List<Piece> Children(string? card) =>
    [
        .. _layout.Groups.Where(group => group.Parent == card && (card is not null || _seen(group.Box)))
            .Select(group => new Piece(group.Id, group.Box, group.Anchor ?? HorizontalAnchor.Scale, true)),
        .. _layout.Objects.Where(o => o.Group == card && o.Hidden != true && (card is not null || _seen(o.Box)))
            .Select(o => new Piece(o.Id, o.Box, _anchorOf(o), false)),
    ];

    /// <summary>
    /// What stands in one holder laid out: the holder's left edge and width on
    /// the screen, its cells (<paramref name="x"/>, <paramref name="w"/>), its
    /// top on the screen, its first row and its rows as designed. Answers how
    /// tall it stands: its design's height and what its lines added.
    /// </summary>
    private double Lay(List<Piece> pieces, double from, double room, int x, int w, double top, int row, int rows)
    {
        var grown = 0.0;
        foreach (var band in Bands(pieces))
        {
            var bandRow = band.Min(piece => piece.Box.Y);
            var bandRows = band.Max(piece => piece.Box.Y + piece.Box.H) - bandRow;
            var bandTop = top + (bandRow - row) * _cell + grown;
            var wrapped = !Fits(band, from, room, x, w);
            List<List<Piece>> lines = wrapped ? Lines(band, room) : [band];
            var lineTop = bandTop;
            foreach (var line in lines)
            {
                var lineRow = line.Min(piece => piece.Box.Y);
                List<(double Left, double Width)> across = wrapped ? Wrapped(line, from, room) : [.. line.Select(piece => Across(piece, from, room, x, w))];
                var lineHeight = 0.0;
                for (var i = 0; i < line.Count; i++)
                {
                    var piece = line[i];
                    var pieceTop = lineTop + (piece.Box.Y - lineRow) * _cell;
                    var height = piece.Card
                        ? Lay(Children(piece.Id), across[i].Left, across[i].Width, piece.Box.X, piece.Box.W, pieceTop, piece.Box.Y, piece.Box.H)
                        : piece.Box.H * _cell;
                    _spots[piece.Id] = new Spot(across[i].Left, pieceTop, across[i].Width, height);
                    lineHeight = Math.Max(lineHeight, pieceTop - lineTop + height);
                }

                lineTop += lineHeight;
            }

            grown += Math.Max(0, lineTop - bandTop - bandRows * _cell);
        }

        // As tall as designed, and taller by what its lines added.
        return rows * _cell + grown;
    }

    /// <summary>The pieces in bands, top to bottom: those whose rows meet stand in one, each band left to right.</summary>
    private static List<List<Piece>> Bands(List<Piece> pieces)
    {
        var bands = new List<List<Piece>>();
        var reach = 0;
        foreach (var piece in pieces.OrderBy(piece => piece.Box.Y).ThenBy(piece => piece.Box.X))
        {
            // A piece that begins below every row of the band so far begins the next one.
            if (bands.Count == 0 || piece.Box.Y >= reach)
            {
                bands.Add([]);
                reach = piece.Box.Y + piece.Box.H;
            }
            else
            {
                reach = Math.Max(reach, piece.Box.Y + piece.Box.H);
            }

            bands[^1].Add(piece);
        }

        return [.. bands.Select(band => band.OrderBy(piece => piece.Box.X).ThenBy(piece => piece.Box.Y).ToList())];
    }

    /// <summary>
    /// A band fits its holder when each of its pieces, placed by its anchor,
    /// stands inside the holder at no less than its own size, and none stands
    /// on another: two cards of 24 columns side by side in a card of 24 do
    /// not, nor do two kept to both edges of a card that narrowed, which
    /// would each be squeezed under its own size. Scale spreads in
    /// proportion, and fits whatever the width.
    /// </summary>
    private bool Fits(List<Piece> band, double from, double room, int x, int w)
    {
        var placed = band.Select(piece => (Piece: piece, Spot: Across(piece, from, room, x, w))).OrderBy(one => one.Spot.Left).ToList();
        for (var i = 0; i < placed.Count; i++)
        {
            var (piece, (left, width)) = placed[i];
            var squeezed = piece.Anchor != HorizontalAnchor.Scale && width < piece.Box.W * _cell - 0.5;
            if (width < 1 || squeezed || left < from - 0.5 || left + width > from + room + 0.5
                || (i > 0 && left < placed[i - 1].Spot.Left + placed[i - 1].Spot.Width - 0.5))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A band too wide broken into lines, left to right: each takes what fits at its own size, and at least one.</summary>
    private List<List<Piece>> Lines(List<Piece> band, double room)
    {
        var lines = new List<List<Piece>>();
        var used = 0.0;
        foreach (var piece in band)
        {
            var width = piece.Box.W * _cell;
            if (lines.Count == 0 || used + width > room + 0.5)
            {
                lines.Add([]);
                used = 0;
            }

            lines[^1].Add(piece);
            used += width;
        }

        return lines;
    }

    /// <summary>
    /// A piece across its holder by its anchor, as fluid always placed it:
    /// spread in proportion to the holder's width (Scale); kept to its left
    /// edge or its right one at its own size; or kept to both and widened by
    /// what the holder gained.
    /// </summary>
    private (double Left, double Width) Across(Piece piece, double from, double room, int x, int w) => piece.Anchor switch
    {
        HorizontalAnchor.Left => (from + (piece.Box.X - x) * _cell, piece.Box.W * _cell),
        HorizontalAnchor.Right => (from + room - (x + w - piece.Box.X) * _cell, piece.Box.W * _cell),
        HorizontalAnchor.Both => (from + (piece.Box.X - x) * _cell, piece.Box.W * _cell + room - w * _cell),
        _ => (from + (piece.Box.X - x) * room / w, piece.Box.W * room / w),
    };

    /// <summary>
    /// A line of a band too wide: Left from the left and Right to the right at
    /// their own size, Both and Scale sharing what is left over in proportion
    /// to their size, the whole line when alone on it. A piece wider than the
    /// line at its own size stays so if it keeps its size (Left, Right), and
    /// takes the line if it gives way (Both, Scale).
    /// </summary>
    private List<(double Left, double Width)> Wrapped(List<Piece> line, double from, double room)
    {
        var own = line.Select(piece => piece.Box.W * _cell).ToList();
        var giving = line.Select(piece => piece.Anchor is HorizontalAnchor.Both or HorizontalAnchor.Scale).ToList();
        var spare = room - own.Sum();
        var shared = Enumerable.Range(0, line.Count).Where(i => giving[i]).Sum(i => own[i]);
        var widths = Enumerable.Range(0, line.Count)
            .Select(i => !giving[i] ? own[i]
                : spare >= 0 ? own[i] + (shared > 0 ? spare * own[i] / shared : 0)
                : room)
            .ToList();

        var spots = new (double Left, double Width)[line.Count];
        var left = from;
        for (var i = 0; i < line.Count; i++)
        {
            if (line[i].Anchor != HorizontalAnchor.Right)
            {
                spots[i] = (left, widths[i]);
                left += widths[i];
            }
        }

        var right = from + room;
        for (var i = line.Count - 1; i >= 0; i--)
        {
            if (line[i].Anchor == HorizontalAnchor.Right)
            {
                right -= widths[i];
                spots[i] = (right, widths[i]);
            }
        }

        return [.. spots];
    }
}
