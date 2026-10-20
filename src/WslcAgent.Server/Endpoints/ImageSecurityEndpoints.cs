using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
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

        return api;
    }
}
