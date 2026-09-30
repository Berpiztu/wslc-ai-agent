using WslcAgent.Server.ClientPackages;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/clients</c>: the native client installers and the version they install. See docs/api-v1.md.</summary>
public static class ClientEndpoints
{
    public static RouteGroupBuilder MapClientEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/clients");

        group.MapGet("/{platform}", (string platform, IClientPackageService packages) => packages.Describe(platform))
            .WithName("DescribeClientPackage");

        group.MapGet("/{platform}/download", (string platform, IClientPackageService packages) =>
            {
                var info = packages.Describe(platform);
                var path = packages.Locate(platform);
                return path is null
                    ? Results.Problem(info.Error, statusCode: StatusCodes.Status404NotFound, title: "Client package not available")
                    : Results.File(path, packages.MediaType(platform), info.Filename);
            })
            .WithName("DownloadClientPackage");

        return api;
    }
}
