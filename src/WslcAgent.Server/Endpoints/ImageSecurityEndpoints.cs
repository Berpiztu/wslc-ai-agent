using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Images;

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

        return api;
    }
}
