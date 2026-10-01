using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Overview;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/home</c>: the Home dashboard's overview and its metrics; <c>/api/v1/me/dashboard-v2</c>, the dashboard kept with the user, with its shipped default and its objects' defaults. See docs/api-v1.md.</summary>
public static class HomeEndpoints
{
    public static RouteGroupBuilder MapHomeEndpoints(this RouteGroupBuilder api)
    {
        // The dashboard as the user keeps it, for every client of this agent:
        // the client's own text, stored and handed back as it was written. No
        // notice is published: the dashboard is read when its page opens.
        api.MapGet("/me/dashboard-v2", (DashboardV2Store store) =>
                Results.Text(store.Get() ?? "", "application/json; charset=utf-8"))
            .WithName("GetUserDashboardV2");

        api.MapPut("/me/dashboard-v2", async (HttpRequest request, DashboardV2Store store) =>
            {
                store.Set(await ReadBodyAsync(request));
                return Results.NoContent();
            })
            .WithName("SetUserDashboardV2");

        // The default as the agent ships it, read by every client: a page or a
        // view left blank, on the agent or on a device, shows the default's.
        // Empty when none is shipped.
        api.MapGet("/dashboard-v2/default", (DashboardV2Store store) =>
                Results.Text(store.Default() ?? "", "application/json; charset=utf-8"))
            .WithName("GetDefaultDashboardV2");

        // Save as default: written back to the repository on a development
        // agent; on an installed one, the views it changes become the agent's
        // own default, over the shipped one.
        api.MapPut("/dashboard-v2/default", async (HttpRequest request, DashboardV2Store store) =>
            {
                store.SetDefault(await ReadBodyAsync(request));
                return Results.NoContent();
            })
            .WithName("SetDefaultDashboardV2");

        // The user's views put back to the default, and the agent's own
        // default views loaded from a file (or the newest shipped or waiting
        // when the body is empty) or dropped back to the shipped ones.
        api.MapPost("/me/dashboard-v2/reset", (string? views, DashboardDefaults files) => Answer(() => files.ResetDashboardViews(ViewList(views))))
            .WithName("ResetUserDashboardV2Views");

        // Every state the dashboard was saved in, each with its origin and
        // revision (v2.3), and any of them brought back; and the whole
        // dashboard back to the release at once.
        api.MapGet("/me/dashboard-v2/history", (DashboardDefaults files) => files.History())
            .WithName("UserDashboardV2History");

        api.MapPost("/me/dashboard-v2/history/{id:int}/restore", (int id, DashboardDefaults files) => Answer(() => files.RestoreRevision(id)))
            .WithName("RestoreUserDashboardV2Revision");

        api.MapPost("/me/dashboard-v2/reset-all", (DashboardDefaults files) => Answer(files.ResetDashboardToRelease))
            .WithName("ResetUserDashboardV2ToRelease");

        api.MapPost("/dashboard-v2/default/views", async (string? views, HttpRequest request, DashboardDefaults files) =>
            {
                var body = await ReadBodyAsync(request);
                return Answer(() => files.LoadDefaultViews(body, ViewList(views)));
            })
            .WithName("LoadDefaultDashboardV2Views");

        api.MapDelete("/dashboard-v2/default/views", (string? views, DashboardDefaults files) => Answer(() => files.ResetDefaultViews(ViewList(views))))
            .WithName("ResetDefaultDashboardV2Views");

        // How each kind of dashboard object is born, in the landscape and the
        // portrait view: read by every client, and written from the board of
        // every object one kind at a time, on any agent — back to the
        // repository on a development one, as its own on an installed one.
        api.MapGet("/dashboard/object-defaults", (ObjectDefaultsStore store) => new ObjectDefaultsResponse(store.Writable, store.Get()))
            .WithName("GetObjectDefaults");

        api.MapPut("/dashboard/object-defaults/{view}/{type}", async (string view, string type, HttpRequest request, ObjectDefaultsStore store) =>
                view is not ("landscape" or "portrait") || !IsObjectType(type)
                    ? Results.Problem("A view is landscape or portrait, and a type is an object's (wslc.container-cpu).", statusCode: StatusCodes.Status400BadRequest)
                    : store.Set(view, type, await ReadBodyAsync(request))
                        ? Results.NoContent()
                        : Results.Problem("The body is not an object's default.", statusCode: StatusCodes.Status400BadRequest))
            .WithName("SetObjectDefault");

        // The two shipped files once they travel on their own, and the user's
        // dashboard carried between agents (docs/dashboard-defaults.md). A
        // body left empty means the file waiting in the package folder.
        var defaults = api.MapGroup("/dashboard/defaults");

        defaults.MapGet("", (DashboardDefaults files) => files.Status())
            .WithName("GetDashboardDefaults");

        defaults.MapPost("/inspect", async (HttpRequest request, DashboardDefaults files) =>
            {
                var body = await ReadBodyAsync(request);
                return Answer(() => files.Inspect(body));
            })
            .WithName("InspectDefaultsFile");

        defaults.MapPost("/object-defaults", async (HttpRequest request, DashboardDefaults files) =>
            {
                var body = await ReadBodyAsync(request);
                return Answer(() => files.LoadObjectDefaults(body));
            })
            .WithName("LoadObjectDefaults");

        defaults.MapDelete("/object-defaults", (DashboardDefaults files) => files.ResetObjectDefaults())
            .WithName("ResetObjectDefaults");

        defaults.MapGet("/object-defaults/export", (DashboardDefaults files) => Download(files.ExportObjectDefaults))
            .WithName("ExportObjectDefaults");

        // The user's own changes to the objects' defaults, kept over the base:
        // dropped all at once, or kept or given up kind by kind where a newer
        // base changed the same kind.
        defaults.MapDelete("/object-defaults/changes", (DashboardDefaults files) => files.ClearObjectDefaultChanges())
            .WithName("ClearObjectDefaultChanges");

        defaults.MapPost("/object-defaults/conflicts", (ObjectDefaultChoice[] choices, DashboardDefaults files) =>
                files.SettleObjectDefaultConflicts(choices))
            .WithName("SettleObjectDefaultConflicts");

        api.MapPost("/me/dashboard-v2/import", async (string? views, HttpRequest request, DashboardDefaults files) =>
            {
                var body = await ReadBodyAsync(request);
                return Answer(() => files.ImportDashboard(body, ViewList(views)));
            })
            .WithName("ImportUserDashboardV2");

        api.MapGet("/me/dashboard-v2/export", (string? views, DashboardDefaults files) => Download(() => files.ExportDashboard(ViewList(views))))
            .WithName("ExportUserDashboardV2");

        var group = api.MapGroup("/home");

        group.MapGet("", (IHomeService home, CancellationToken ct) => home.OverviewAsync(ct))
            .WithName("HomeOverview");

        group.MapGet("/metrics/runtime", (IHomeService home, CancellationToken ct) => home.RuntimeAsync(ct))
            .WithName("HomeRuntime");

        group.MapGet("/metrics/io", (IHomeService home, CancellationToken ct) => home.IoAsync(ct))
            .WithName("HomeIo");

        group.MapGet("/metrics/disk", (IHomeService home) => home.Disk())
            .WithName("HomeDisk");

        return api;
    }

    /// <summary>An object's type: lower-case letters, digits, hyphens and the dot after its owner (wslc.container-cpu).</summary>
    private static bool IsObjectType(string type) =>
        type.Length is > 0 and <= 96 && type.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.');

    /// <summary>The answer of a defaults verb, or the reason it was refused as a problem.</summary>
    private static IResult Answer<T>(Func<T> verb)
    {
        try
        {
            return Results.Ok(verb());
        }
        catch (DefaultsRefusedException refused)
        {
            return Results.Problem(refused.Message, statusCode: refused.Status);
        }
    }

    /// <summary>A defaults file written for download, or the reason it was refused as a problem.</summary>
    private static IResult Download(Func<string> export)
    {
        try
        {
            return Results.Text(export(), "application/json; charset=utf-8");
        }
        catch (DefaultsRefusedException refused)
        {
            return Results.Problem(refused.Message, statusCode: refused.Status);
        }
    }

    /// <summary>A dashboard's views as a query names them: <c>system-landscape,user-portrait</c>.</summary>
    private static string[] ViewList(string? views) =>
        (views ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A dashboard body is the client's own text, taken as it came.</summary>
    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }
}
