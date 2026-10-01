using System.Text.Json.Nodes;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Overview;

/// <summary>
/// A dashboard's views, as its text keeps them (Berpiztu.Dashboard's
/// DashboardPages): the main page — System — at the top, its landscape layout
/// with its portrait one under <c>portrait</c>, and every other page under
/// <c>pages</c> by its name, laid out the same way. A view is named by its
/// page and its orientation (<see cref="DashboardViewNames"/>); the agent
/// reads no further than that, and each layout is copied as it is.
/// </summary>
public static class StoredDashboardViews
{
    private const string MainPage = "system";

    private const string Pages = "pages";

    private const string Portrait = "portrait";

    private const string Landscape = "landscape";

    /// <summary>The views that hold objects, the main page's first.</summary>
    public static IReadOnlyList<string> Present(JsonObject dashboard)
    {
        var found = new List<string>();
        foreach (var (page, layout) in PagesOf(dashboard))
        {
            if (HasObjects(layout))
            {
                found.Add(Name(page, Landscape));
            }

            if (layout[Portrait] is JsonObject portrait && HasObjects(portrait))
            {
                found.Add(Name(page, Portrait));
            }
        }

        return found;
    }

    /// <summary>A dashboard holding only the views named, each as it was.</summary>
    public static JsonObject Extract(JsonObject dashboard, IReadOnlyCollection<string> views)
    {
        var extract = new JsonObject();
        foreach (var (page, layout) in PagesOf(dashboard))
        {
            var landscape = views.Contains(Name(page, Landscape));
            var portrait = views.Contains(Name(page, Portrait)) && layout[Portrait] is JsonObject;
            if (!landscape && !portrait)
            {
                continue;
            }

            var target = PageOf(extract, page);
            if (landscape)
            {
                CopyLandscape(layout, target);
            }

            if (portrait)
            {
                target[Portrait] = layout[Portrait]!.DeepClone();
            }
        }

        return extract;
    }

    /// <summary>
    /// <paramref name="into"/> with the views named taken from
    /// <paramref name="from"/>, each replacing its own; every other view, and
    /// every other page, kept as it was. A view named that <paramref name="from"/>
    /// does not hold changes nothing.
    /// </summary>
    public static JsonObject Merge(JsonObject into, JsonObject from, IReadOnlyCollection<string> views)
    {
        var merged = (JsonObject)into.DeepClone();
        foreach (var (page, layout) in PagesOf(from))
        {
            if (views.Contains(Name(page, Landscape)) && HasObjects(layout))
            {
                var target = PageOf(merged, page);
                foreach (var key in target.Select(pair => pair.Key).Where(key => key is not (Portrait or Pages)).ToList())
                {
                    target.Remove(key);
                }

                CopyLandscape(layout, target);
            }

            if (views.Contains(Name(page, Portrait)) && layout[Portrait] is JsonObject portrait && HasObjects(portrait))
            {
                PageOf(merged, page)[Portrait] = portrait.DeepClone();
            }
        }

        return merged;
    }

    /// <summary><paramref name="dashboard"/> without the views named; every other view, and every other page, kept as it was.</summary>
    public static JsonObject Remove(JsonObject dashboard, IReadOnlyCollection<string> views)
    {
        var left = (JsonObject)dashboard.DeepClone();
        foreach (var (page, layout) in PagesOf(left).ToList())
        {
            if (views.Contains(Name(page, Landscape)))
            {
                foreach (var key in layout.Select(pair => pair.Key).Where(key => key is not (Portrait or Pages)).ToList())
                {
                    layout.Remove(key);
                }
            }

            if (views.Contains(Name(page, Portrait)))
            {
                layout.Remove(Portrait);
            }
        }

        return left;
    }

    /// <summary>The layout of the view named — its objects and cards — or null where the dashboard has none.</summary>
    public static JsonObject? Layout(JsonObject dashboard, string view)
    {
        foreach (var (page, layout) in PagesOf(dashboard))
        {
            if (view == Name(page, Landscape))
            {
                return layout;
            }

            if (view == Name(page, Portrait))
            {
                return layout[Portrait] as JsonObject;
            }
        }

        return null;
    }

    /// <summary>Whether the view named is the same in both dashboards, missing from both included.</summary>
    public static bool Same(JsonObject first, JsonObject second, string view) =>
        JsonNode.DeepEquals(Extract(first, [view]), Extract(second, [view]));

    private static string Name(string page, string orientation) => $"{page}-{orientation}";

    private static IEnumerable<(string Page, JsonObject Layout)> PagesOf(JsonObject dashboard)
    {
        yield return (MainPage, dashboard);
        if (dashboard[Pages] is JsonObject pages)
        {
            foreach (var (name, node) in pages)
            {
                if (node is JsonObject layout)
                {
                    yield return (name, layout);
                }
            }
        }
    }

    /// <summary>A page's layout in <paramref name="dashboard"/>, made where it is missing.</summary>
    private static JsonObject PageOf(JsonObject dashboard, string page)
    {
        if (page == MainPage)
        {
            return dashboard;
        }

        if (dashboard[Pages] is not JsonObject pages)
        {
            dashboard[Pages] = pages = [];
        }

        if (pages[page] is not JsonObject layout)
        {
            pages[page] = layout = [];
        }

        return layout;
    }

    /// <summary>A page's landscape layout — everything but its portrait one and, on the main page, the other pages — copied onto <paramref name="target"/>.</summary>
    private static void CopyLandscape(JsonObject layout, JsonObject target)
    {
        foreach (var (key, value) in layout)
        {
            if (key is not (Portrait or Pages))
            {
                target[key] = value?.DeepClone();
            }
        }
    }

    private static bool HasObjects(JsonObject layout) => layout["objects"] is JsonArray { Count: > 0 };
}
