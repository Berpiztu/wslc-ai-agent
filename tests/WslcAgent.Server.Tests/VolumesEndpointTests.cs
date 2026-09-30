using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class VolumesEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Usage = "container list --all --format json";

    [Fact]
    public async Task List_maps_rows_reads_na_as_empty_and_marks_mounted_volumes()
    {
        var runner = new FakeWslcRunner()
            .Answer("volume list --format json", FakeWslcRunner.Fixture("volume-list.ndjson"))
            .Answer(Usage, FakeWslcRunner.Fixture("container-list-usage.ndjson"))
            .Answer("container stats --all --format json", FakeWslcRunner.Fixture("container-stats-usage.ndjson"));

        var body = await factory.ClientWith(runner).GetFromJsonAsync<VolumeListResponse>("/api/v1/volumes");

        Assert.NotNull(body);
        Assert.Equal(3, body.Count);
        var used = body.Volumes.Single(v => v.Name == "a0_usr");
        Assert.True(used.InUse);
        Assert.Equal((1, 1), (used.Containers, used.Running));
        Assert.Equal((2_000_000L, 1_000_000L), (used.DiskReadBytes, used.DiskWriteBytes));
        Assert.Equal((2_000_000L, 1_000_000L), (body.DiskReadBytes, body.DiskWriteBytes));
        Assert.True(used.MountpointIsInsideVm);
        Assert.False(body.Volumes.Single(v => v.Name == "prueba").InUse);
        Assert.Equal((0, 0), (body.Volumes.Single(v => v.Name == "prueba").Containers, body.Volumes.Single(v => v.Name == "prueba").Running));
        Assert.Equal("", body.Volumes.Single(v => v.Name == "prueba").Mountpoint);
    }

    [Fact]
    public async Task Create_builds_the_vhd_options_from_the_request()
    {
        var runner = new FakeWslcRunner().Answer("volume create --driver vhd --opt SizeBytes=8000000000 --opt Fixed=true --label env=prod data", "");

        var response = await factory.ClientWith(runner).PostAsJsonAsync("/api/v1/volumes", new CreateVolumeRequest("data", "vhd", "8GB", Fixed: true, Label: "env=prod"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task Create_ignores_size_for_the_guest_driver_and_rejects_option_like_names()
    {
        var runner = new FakeWslcRunner().Answer("volume create data", "");
        var client = factory.ClientWith(runner);

        var created = await client.PostAsJsonAsync("/api/v1/volumes", new CreateVolumeRequest("data", Size: "8GB"));
        var rejected = await client.PostAsJsonAsync("/api/v1/volumes", new CreateVolumeRequest("--rm"));

        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(["volume", "create", "data"], runner.Calls.Single());
    }

    [Fact]
    public async Task Delete_and_prune_run_the_matching_commands()
    {
        var runner = new FakeWslcRunner()
            .Answer("volume remove a0_usr", "")
            .Answer("volume prune -f -a", "");
        var client = factory.ClientWith(runner);

        var removed = await client.DeleteAsync("/api/v1/volumes/a0_usr");
        var pruned = await client.PostAsync("/api/v1/volumes/prune", content: null);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, pruned.StatusCode);
        Assert.Equal(2, runner.Calls.Count);
    }
}
