using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Registries;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/registry</c>: log in and out of a container registry, and the logins made. See docs/api-v1.md.</summary>
public static class RegistryEndpoints
{
    public static RouteGroupBuilder MapRegistryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/registry");

        group.MapGet("/logins", (RegistryService registry) => registry.Logins())
            .WithName("RegistryLogins");

        group.MapPost("/login", async Task<NoContent> (RegistryLoginRequest request, RegistryService registry, CancellationToken ct) =>
            {
                await registry.LoginAsync(request, ct);
                return TypedResults.NoContent();
            })
            .WithName("RegistryLogin");

        group.MapPost("/logout", async Task<NoContent> (RegistryLogoutRequest request, RegistryService registry, CancellationToken ct) =>
            {
                await registry.LogoutAsync(request, ct);
                return TypedResults.NoContent();
            })
            .WithName("RegistryLogout");

        return api;
    }
}
