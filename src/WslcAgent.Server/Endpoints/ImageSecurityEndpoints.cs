using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Auth;
using WslcAgent.Server.Images;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/image-security</c>: Settings → Security → Image security check. See docs/api-v1.md.</summary>
public static class ImageSecurityEndpoints
{
    public static RouteGroupBuilder MapImageSecurityEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/image-security", (ImageSecuritySettingsStore store) => store.Get())
            .WithName("GetImageSecuritySettings");

        api.MapPut("/image-security", (ImageSecuritySettings settings, ImageSecuritySettingsStore store) =>
                ImageSecurityPolicy.IsKnown(settings.Policy)
                    ? Results.Ok(store.Set(settings))
                    : Results.Problem($"policy is {ImageSecurityPolicy.Off}, {ImageSecurityPolicy.Warn} or {ImageSecurityPolicy.Block}, not '{settings.Policy}'.",
                        statusCode: StatusCodes.Status400BadRequest, title: "Unknown policy"))
            .WithName("SetImageSecuritySettings");

        // Zaindari's window: the image checked now, every library and every known vulnerability;
        // what is found marks the image, worse, better or clean, and its containers read it.
        api.MapGet("/image-security/vulnerabilities", async (string image, IImageSecurity security, WslcEvents events, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(image))
                {
                    throw new ArgumentException("image is required", nameof(image));
                }

                var report = await security.ReportAsync(image, ct);
                events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
                return report;
            })
            .WithName("GetImageVulnerabilities");

        // Zaindari's patch: the image built again with its system packages upgraded, the containers chosen moved to it.
        api.MapPost("/image-security/patch", (PatchImageRequest request, ImagePatches patches, CancellationToken ct) => patches.StartAsync(request, ct))
            .WithName("PatchImage");

        // The patch's Python lines tried first, in a container of the image, nothing written: what each would change.
        api.MapPost("/image-security/patch/dry-run", (PatchDryRunRequest request, PatchDryRuns runs, CancellationToken ct) => runs.RunAsync(request, ct))
            .WithName("DryRunPatch");

        // What a person accepted: a vulnerability nobody can mend today, in a part the image does not use,
        // said with its reason and no longer raised while no version fixes it. The image's colour changes
        // at its next check (Zaindari's window, a new container).
        api.MapGet("/image-security/acceptances", (ImageAcceptanceStore store) => store.Get())
            .WithName("GetImageAcceptances");

        api.MapPost("/image-security/acceptances", (AcceptImageVulnerability request, ImageAcceptanceStore store, AgentLogin login, HttpContext http, WslcEvents events) =>
            {
                if (new[] { request.Image, request.Ecosystem, request.Package, request.Advisory }.Any(string.IsNullOrWhiteSpace))
                {
                    return Results.Problem("image, ecosystem, package and advisory are required.", statusCode: StatusCodes.Status400BadRequest, title: "Incomplete");
                }

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    return Results.Problem("An acceptance says why: reason is required.", statusCode: StatusCodes.Status400BadRequest, title: "No reason");
                }

                var accepted = store.Accept(request, Caller(AgentAuth.Identify(http, login)));
                events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
                return Results.Ok(accepted);
            })
            .WithName("AcceptImageVulnerability");

        api.MapDelete("/image-security/acceptances", (string image, string ecosystem, string package, string advisory, ImageAcceptanceStore store, WslcEvents events) =>
            {
                if (!store.Withdraw(image, ecosystem, package, advisory))
                {
                    return Results.NotFound();
                }

                events.Publish(new ChangeNotice([ChangeNotice.Container, ChangeNotice.Image]));
                return Results.NoContent();
            })
            .WithName("WithdrawImageAcceptance");

        return api;
    }

    /// <summary>Who accepted, as an acceptance keeps it: the user's name, or <c>local</c>, <c>token</c>.</summary>
    private static string Caller(string? identity) => identity switch
    {
        null => "",
        _ when identity.StartsWith("user:", StringComparison.Ordinal) => identity["user:".Length..],
        _ => identity,
    };
}
