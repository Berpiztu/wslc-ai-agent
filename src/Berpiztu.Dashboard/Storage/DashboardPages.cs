using System.Text.Json.Nodes;
using Berpiztu.Dashboard.Model;

namespace Berpiztu.Dashboard.Storage;

/// <summary>
/// A dashboard's pages kept as one text (tabs, so each is seen at a glance):
/// the main page as a dashboard always was —
/// its landscape layout, its portrait one under <c>portrait</c> — and every other
/// page, by its name, under <c>pages</c>, each with its two views the same
/// way. A text written before there were pages is the main page alone.
/// </summary>
public static class DashboardPages
{
    private const string PagesKey = "pages";

    /// <summary>A page's text — its two views — as it is kept; the main page for a null name; null for a page never laid out.</summary>
    public static string? Read(string? stored, string? page)
    {
        if (StoredText.Parse(stored) is not { } whole)
        {
            return page is null ? stored : null;
        }

        if (page is not null)
        {
            return whole[PagesKey]?[page]?.ToJsonString();
        }

        whole.Remove(PagesKey);
        return whole.ToJsonString();
    }

    /// <summary>The whole text with one page's written in it, every other page kept as it was.</summary>
    public static string Write(string? stored, string? page, string text)
    {
        var whole = StoredText.Parse(stored) ?? [];
        if (page is not null)
        {
            if (whole[PagesKey] is not JsonObject pages)
            {
                whole[PagesKey] = pages = [];
            }

            pages[page] = JsonNode.Parse(text);
            return whole.ToJsonString();
        }

        var main = JsonNode.Parse(text) as JsonObject ?? [];
        main.Remove(PagesKey);
        if (whole[PagesKey] is { } others)
        {
            main[PagesKey] = others.DeepClone();
        }

        return main.ToJsonString();
    }

    /// <summary>The names of the pages the text holds besides the main one.</summary>
    public static IReadOnlyList<string> Named(string? stored) =>
        StoredText.Parse(stored)?[PagesKey] is JsonObject pages ? [.. pages.Select(page => page.Key)] : [];

    /// <summary>
    /// The text with every page's view that is blank — never laid out, or
    /// holding no object — taken from <paramref name="fallback"/>'s same page
    /// and view where that one holds some (a dashboard left blank shows the
    /// default); the rest kept as it is.
    /// </summary>
    public static string? Filled(string? stored, string? fallback)
    {
        if (StoredText.Parse(fallback) is null)
        {
            return stored;
        }

        var filled = stored;
        foreach (var page in new string?[] { null }.Concat(Named(fallback)))
        {
            var from = Read(fallback, page);
            var text = Read(filled, page);
            foreach (var view in new[] { DashboardView.Landscape, DashboardView.Portrait })
            {
                if (Blank(DashboardViews.Read(text, view)) && DashboardViews.Read(from, view) is { } given && !Blank(given))
                {
                    text = DashboardViews.Write(text, view, given);
                }
            }

            if (text is not null)
            {
                filled = Write(filled, page, text);
            }
        }

        return filled;
    }

    /// <summary>A view's layout that shows nothing: never laid out, or holding no object.</summary>
    private static bool Blank(string? layout) => layout is null || DashboardLayout.Read(layout).Objects.Count == 0;

    /// <summary>Every layout the text holds — each page's, in each view — changed as <paramref name="change"/> says, the rest kept.</summary>
    public static string Each(string stored, Func<DashboardLayout, DashboardLayout> change) =>
        new string?[] { null }.Concat(Named(stored)).Aggregate(stored, (whole, page) =>
            Read(whole, page) is { } text
                ? Write(whole, page, new[] { DashboardView.Landscape, DashboardView.Portrait }.Aggregate(text, (views, view) =>
                    DashboardViews.Read(views, view) is { } layout
                        ? DashboardViews.Write(views, view, change(DashboardLayout.Read(layout)).Write())
                        : views))
                : whole);
}
