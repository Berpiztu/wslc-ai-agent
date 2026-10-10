namespace Berpiztu.Wslc.Compose;

/// <summary>
/// Rows printed in columns, each as wide as its widest value, three spaces
/// apart: what <c>ps</c> and <c>ls</c> print. The head in bold, and a cell in
/// the colour its command gives it (<c>style</c>, by row and column), which
/// is put on after the cell is padded: a colour is characters too, and
/// counted as width it would push the columns apart.
/// </summary>
internal static class Table
{
    private const string Gap = "   ";

    public static void Write(IReadOnlyList<string> head, IReadOnlyList<IReadOnlyList<string>> rows, Func<int, int, string>? style = null)
    {
        var widths = head.Select((title, column) => rows.Select(row => row[column].Length).Append(title.Length).Max()).ToList();
        Terminal.Line(Row(head, widths, _ => Terminal.Bold));
        for (var row = 0; row < rows.Count; row++)
        {
            var at = row;
            Terminal.Line(Row(rows[row], widths, column => style?.Invoke(at, column) ?? ""));
        }
    }

    /// <summary>The last column is not padded: nothing stands after it, and spaces there only wrap the line.</summary>
    private static string Row(IReadOnlyList<string> cells, IReadOnlyList<int> widths, Func<int, string> style) =>
        string.Join(Gap, cells.Select((value, column) => Terminal.Tint(column == cells.Count - 1 ? value : value.PadRight(widths[column]), style(column))));
}
