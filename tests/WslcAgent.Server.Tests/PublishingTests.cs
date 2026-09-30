using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Publishing;

namespace WslcAgent.Server.Tests;

/// <summary>
/// Publish: a container port on a public name.
/// The map is the proxy's whole configuration; a save with public names does the
/// four manual steps, and one that cannot be done fails before anything moves.
/// </summary>
public sealed class PublishingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Inspect = "container inspect web --format json";
    private const string EmptyNetwork = """[{"Name":"published","Id":"f77b","Containers":{}}]""";
    private const string WebOnNetwork = """[{"Name":"published","Id":"f77b","Containers":{"abc":{"Name":"web","IPv4Address":"172.20.0.2/16"},"p":{"Name":"proxy","IPv4Address":"172.20.0.3/16"}}}]""";
    private const string ProxyOnNetwork = """[{"Name":"published","Id":"f77b","Containers":{"p":{"Name":"proxy","IPv4Address":"172.20.0.3/16"}}}]""";

    [Fact]
    public void The_map_writes_one_line_per_name_and_reads_them_back()
    {
        var names = new[]
        {
            new Publication("open-webui", 8080, "webui-home.example.com"),
            new Publication("api", 8000, "api-home.example.com"),
        };

        var text = PublishedMap.Render(names);

        Assert.Contains($"{"webui-home.example.com",-28} http://open-webui:8080;", text);
        Assert.Contains("resolver 127.0.0.11", text);
        Assert.Contains("return 404;", text);
        Assert.Equal(names.OrderBy(n => n.Hostname, StringComparer.Ordinal), PublishedMap.Parse(text));
    }

    [Fact]
    public async Task Publishing_writes_the_map_and_restarts_the_proxy_and_unpublishing_takes_the_line_out()
    {
        var runner = new FakeWslcRunner()
            .Answer("network inspect published --format json", WebOnNetwork)
            .Answer("container restart proxy", "");
        var client = factory.ClientWith(runner);
        var map = Path.Combine(TestHost.TempDataDirectory(), "published.conf");
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", map));

        var published = await client.PostAsJsonAsync("/api/v1/publications", new PublishRequest("web", 8080, "web-pc.example.test"));
        var listed = await client.GetFromJsonAsync<PublicationList>("/api/v1/publications");
        var unpublished = await client.DeleteAsync("/api/v1/publications/web-pc.example.test");
        var afterwards = await client.GetFromJsonAsync<PublicationList>("/api/v1/publications");

        Assert.Equal(HttpStatusCode.Created, published.StatusCode);
        Assert.Equal([new Publication("web", 8080, "web-pc.example.test")], listed!.Publications);
        Assert.Equal(HttpStatusCode.NoContent, unpublished.StatusCode);
        Assert.Empty(afterwards!.Publications);
        Assert.DoesNotContain("web-pc.example.test", await File.ReadAllTextAsync(map));
        Assert.DoesNotContain(runner.Calls, c => c[0] == "network" && c[1] == "connect");
        Assert.Equal(2, runner.Calls.Count(c => c[0] == "container" && c[1] == "restart"));
    }

    [Fact]
    public async Task A_container_that_is_not_on_the_network_is_not_published_and_not_attached()
    {
        var runner = new FakeWslcRunner().Answer("network inspect published --format json", EmptyNetwork);
        var client = factory.ClientWith(runner);
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", Path.Combine(TestHost.TempDataDirectory(), "published.conf")));

        var response = await client.PostAsJsonAsync("/api/v1/publications", new PublishRequest("web", 8080, "web-pc.example.test"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not on the network 'published'", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(runner.Calls, c => c[0] == "network" && c[1] == "connect");
    }

    [Fact]
    public async Task A_save_with_reverse_proxy_ticked_and_no_network_shared_with_the_proxy_fails_before_the_container_is_touched()
    {
        // No network in the form at all, and a network the proxy is not on: neither is shared.
        var runner = new FakeWslcRunner()
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"))
            .Answer("network inspect other --format json", EmptyNetwork);
        var client = factory.ClientWith(runner);
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", Path.Combine(TestHost.TempDataDirectory(), "published.conf")));

        var none = await client.PostAsJsonAsync("/api/v1/containers/web/recreate",
            new ContainerLaunchRequest { Image = "nginx", Name = "web", PublicNames = ["80:web"] });
        var unshared = await client.PostAsJsonAsync("/api/v1/containers/web/recreate",
            new ContainerLaunchRequest { Image = "nginx", Name = "web", ConnectNetworks = ["other"], PublicNames = ["80:web"] });

        Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unshared.StatusCode);
        Assert.Contains("share a user-defined network", await unshared.Content.ReadAsStringAsync());
        Assert.DoesNotContain(runner.Calls, c => c[0] == "container" && c[1] is "rm" or "stop" or "run" or "create");
    }

    [Fact]
    public async Task Set_up_creates_the_network_writes_the_map_and_runs_the_proxy_once()
    {
        var map = Path.Combine(TestHost.TempDataDirectory(), "published.conf");
        var runner = new FakeWslcRunner()
            .Fail("network inspect published --format json", "network published not found")
            .Answer("network create published", "")
            .Answer("container list --all --format json", FakeWslcRunner.Fixture("container-list.ndjson"))
            .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats.ndjson"))
            .Answer($"container run --detach --name proxy --publish 127.0.0.1:8081:80 --volume {map.Replace('\\', '/')}:/etc/nginx/conf.d/default.conf:ro nginx:alpine", "beefbeefbeef\n")
            .Answer("network connect published proxy", "");
        var client = factory.ClientWith(runner);
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", map));

        var response = await client.PostAsJsonAsync("/api/v1/publishing/setup", new { });
        var result = await response.Content.ReadFromJsonAsync<PublishingSetupResult>();
        var policy = await client.GetFromJsonAsync<RestartPolicyInfo>("/api/v1/containers/proxy/restart-policy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result!.NetworkCreated);
        Assert.True(result.MapWritten);
        Assert.True(result.ProxyCreated);
        Assert.Contains("resolver 127.0.0.11", await File.ReadAllTextAsync(map));
        Assert.Equal("always", policy!.Policy);
        Assert.Contains(runner.Calls, c => c[0] == "network" && c[1] == "create");
        Assert.Contains(runner.Calls, c => c[0] == "network" && c[1] == "connect");
    }

    [Fact]
    public async Task A_name_under_another_domain_and_a_name_held_by_another_container_are_refused()
    {
        var runner = new FakeWslcRunner()
            .Answer("network inspect published --format json", WebOnNetwork)
            .Answer("container restart proxy", "");
        var client = factory.ClientWith(runner);
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", Path.Combine(TestHost.TempDataDirectory(), "published.conf")));
        await client.PostAsJsonAsync("/api/v1/publications", new PublishRequest("web", 8080, "web-pc.example.test"));

        var elsewhere = await client.PostAsJsonAsync("/api/v1/publications", new PublishRequest("web", 8080, "web.other.test"));
        var taken = await client.PostAsJsonAsync("/api/v1/publications", new PublishRequest("api", 9000, "web-pc.example.test"));

        Assert.Equal(HttpStatusCode.BadRequest, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
    }

    [Fact]
    public async Task A_save_with_public_names_fails_before_the_container_is_touched_when_the_network_is_missing()
    {
        var runner = new FakeWslcRunner()
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"))
            .Fail("network inspect published --format json", "network published not found");
        var client = factory.ClientWith(runner);
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", Path.Combine(TestHost.TempDataDirectory(), "published.conf")));

        var response = await client.PostAsJsonAsync("/api/v1/containers/web/recreate",
            new ContainerLaunchRequest { Image = "nginx", Name = "web", ConnectNetworks = ["published"], PublicNames = ["80:web"] });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("network 'published'", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(runner.Calls, c => c[0] == "container" && c[1] is "rm" or "stop" or "run" or "create");
    }

    [Fact]
    public async Task Saving_the_form_publishes_the_rows_and_reads_them_back_in_the_details()
    {
        // The recreate rehearses the new settings under a throwaway name first,
        // extra networks included, and removes it before touching the container.
        var rehearsal = ContainerService.RehearsalName("web");
        var runner = new FakeWslcRunner()
            .Answer(Inspect, FakeWslcRunner.Fixture("container-inspect.json"))
            .Answer($"container rm --force {rehearsal}", "")
            .Answer($"container run --detach --name {rehearsal} nginx", "beefbeefbeef\n")
            .Answer($"network connect published {rehearsal}", "")
            .Answer("container stop web", "")
            .Answer("container rm --force web", "")
            .Answer("container run --detach --name web nginx", "cafecafecafe\n")
            .Answer("network inspect published --format json", ProxyOnNetwork)
            .Answer("network connect published web", "")
            .Answer("container restart proxy", "");
        var client = factory.ClientWith(runner);
        var map = Path.Combine(TestHost.TempDataDirectory(), "published.conf");
        await client.PutAsJsonAsync("/api/v1/publishing/settings", new PublishingSettings("example.test", "-pc", "proxy", "published", map));

        // The network is the form's: published, which the proxy is on, among its Networks rows, connected as every extra network is.
        var response = await client.PostAsJsonAsync("/api/v1/containers/web/recreate",
            new ContainerLaunchRequest { Image = "nginx", Name = "web", ConnectNetworks = ["published"], PublicNames = ["80:web"] });
        var details = await client.GetFromJsonAsync<ContainerDetails>("/api/v1/containers/web/details");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await File.ReadAllTextAsync(map);
        Assert.Contains("web-pc.example.test", text);
        Assert.Contains("http://web:80;", text);
        Assert.Equal(["80:web"], details!.Form.PublicNames);
        Assert.Equal([new Publication("web", 80, "web-pc.example.test")], details.Publications);
    }
}
