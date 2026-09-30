namespace Berpiztu.Dashboard.Model;

/// <summary>A rectangle of the canvas's cells: where an object or a group stands and what it spans.</summary>
public readonly record struct CellBox(int X, int Y, int W, int H)
{
    /// <summary>Whether the two take any cell in common.</summary>
    public bool Overlaps(CellBox other) =>
        X < other.X + other.W && other.X < X + W && Y < other.Y + other.H && other.Y < Y + H;

    /// <summary>Whether every cell of <paramref name="other"/> is one of this box's.</summary>
    public bool Contains(CellBox other) =>
        other.X >= X && other.Y >= Y && other.X + other.W <= X + W && other.Y + other.H <= Y + H;

    public CellBox Moved(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>The smallest box holding every one of <paramref name="boxes"/>; there must be at least one.</summary>
    public static CellBox Around(IEnumerable<CellBox> boxes)
    {
        var all = boxes.ToList();
        var left = all.Min(box => box.X);
        var top = all.Min(box => box.Y);
        return new CellBox(left, top, all.Max(box => box.X + box.W) - left, all.Max(box => box.Y + box.H) - top);
    }
}
