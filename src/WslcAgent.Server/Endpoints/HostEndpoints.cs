using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/host</c>: the agent machine's folders, for bind-mount pickers. See docs/api-v1.md.</summary>
public static class HostEndpoints
{
    public static RouteGroupBuilder MapHostEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/host");

        group.MapGet("/folders", (string path = "") => HostFolders.List(path))
            .WithName("ListHostFolders");

        // The answer is the parent listed again, with the new folder in it: the
        // picker stays where it was and selects it.
        group.MapPost("/folders", (CreateHostFolderRequest request) =>
            {
                HostFolders.Create(request.Parent, request.Name);
                return HostFolders.List(request.Parent);
            })
            .WithName("CreateHostFolder");

        return api;
    }
}
