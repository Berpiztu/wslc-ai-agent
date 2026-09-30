using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Testing;

namespace WslcAgent.Server.Endpoints;

/// <summary><c>/api/v1/testing</c>: Settings → Testing (simulate remote access). See docs/api-v1.md.</summary>
public static class TestingEndpoints
{
    public static RouteGroupBuilder MapTestingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/testing", (TestingSettingsStore store) => store.Get())
            .WithName("GetTestingSettings");

        api.MapPut("/testing", (TestingSettings settings, TestingSettingsStore store) => store.Set(settings))
            .WithName("SetTestingSettings");

        return api;
    }
}
