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

        // Written back to the repository from the server's dashboard on a
        // development agent; a release build, which ships it, answers 409.
        api.MapPut("/dashboard-v2/default", async (HttpRequest request, DashboardV2Store store) =>
                store.SetDefault(await ReadBodyAsync(request))
                    ? Results.NoContent()
                    : Results.Problem("Only a development build of the agent writes the default dashboard.", statusCode: StatusCodes.Status409Conflict))
            .WithName("SetDefaultDashboardV2");

        // How each kind of dashboard object is born, in the landscape and the
        // portrait view: read by every client, and written back to the
        // repository from the board of every object on a development agent,
        // one kind at a time; a release build, which ships them, answers 409.
        api.MapGet("/dashboard/object-defaults", (ObjectDefaultsStore store) => new ObjectDefaultsResponse(store.Writable, store.Get()))
            .WithName("GetObjectDefaults");

        api.MapPut("/dashboard/object-defaults/{view}/{type}", async (string view, string type, HttpRequest request, ObjectDefaultsStore store) =>
                view is not ("landscape" or "portrait") || !IsObjectType(type)
                    ? Results.Problem("A view is landscape or portrait, and a type is an object's (wslc.container-cpu).", statusCode: StatusCodes.Status400BadRequest)
                    : store.Set(view, type, await ReadBodyAsync(request))
                        ? Results.NoContent()
                        : Results.Problem("Only a development build of the agent writes the objects' defaults.", statusCode: StatusCodes.Status409Conflict))
            .WithName("SetObjectDefault");

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

    /// <summary>A dashboard body is the client's own text, taken as it came.</summary>
    private static async Task<string> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }
}
