using System.Globalization;
using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Designer;

/// <summary>
/// A proportion the view's size keeps while it is typed (the owner's request
/// of 7 October 2026): with one chosen, the columns typed give the rows and
/// the rows the columns, so the view keeps the shape of the screen it is meant
/// for; Free leaves each to its own field. Cells are square, so the view's
/// columns and rows are its proportion.
/// </summary>
/// <param name="Name">What the field shows: 16:9, Free.</param>
/// <param name="Across">Its width's share; with <paramref name="Down"/>, none for Free.</param>
/// <param name="Down">Its height's share.</param>
public sealed record CanvasProportion(string Name, double Across, double Down)
{
    public const string FreeName = "Free";

    public static CanvasProportion Free { get; } = new(FreeName, 0, 0);

    /// <summary>How far a shape may stand from a named proportion, whole cells being what they are, and still be called by its name.</summary>
    private const double Tolerance = 0.015;

    private static readonly CanvasProportion[] Landscape = [new("16:9", 16, 9), new("16:10", 16, 10), new("4:3", 4, 3), new("21:9", 21, 9)];

    private static readonly CanvasProportion[] Portrait = [new("9:16", 9, 16), new("10:16", 10, 16), new("3:4", 3, 4), new("9:20", 9, 20)];

    public bool IsFree => Across <= 0 || Down <= 0;

    /// <summary>The rows these columns take in this proportion, at least one.</summary>
    public int RowsFor(int columns) => Math.Max(1, (int)Math.Round(columns * Down / Across, MidpointRounding.AwayFromZero));

    /// <summary>The columns these rows take in this proportion, at least one.</summary>
    public int ColumnsFor(int rows) => Math.Max(1, (int)Math.Round(rows * Across / Down, MidpointRounding.AwayFromZero));

    /// <summary>The view's size is this proportion's, as near as whole cells come.</summary>
    public bool Fits(int columns, int rows) => !IsFree && columns > 0 && (RowsFor(columns) == rows || ColumnsFor(rows) == columns);

    /// <summary>The proportions offered for a view: Free, then the screens' own, upright for the portrait view.</summary>
    public static IReadOnlyList<CanvasProportion> For(string view) =>
        [Free, .. view == DashboardView.Portrait ? Portrait : Landscape];

    /// <summary>A shape by the name of the proportion it is, or as width to one: 16:9, 0.6:1.</summary>
    public static string Describe(double width, double height)
    {
        var shape = width / height;
        return Landscape.Concat(Portrait).FirstOrDefault(p => Math.Abs(p.Across / p.Down - shape) / shape <= Tolerance)?.Name
            ?? $"{shape.ToString("0.##", CultureInfo.InvariantCulture)}:1";
    }
}
