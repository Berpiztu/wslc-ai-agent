using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Images;

namespace WslcAgent.Server.Tests;

/// <summary>Build as a followed job, and Import and Load from an uploaded archive.</summary>
public sealed class ImageArchivesTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Build_args_repeat_args_and_labels_per_line()
    {
        var args = ImageBuilds.Args(new BuildImageRequest(@"C:\src\app", "app:1", "Dockerfile.dev", "", "A=1\nB=2\n", "team=x", NoCache: true));

        Assert.Equal(
            ["build", "--progress", "plain", "--tag", "app:1", "--file", "Dockerfile.dev", "--build-arg", "A=1", "--build-arg", "B=2", "--label", "team=x", "--no-cache", @"C:\src\app"],
            args);
    }

    [Fact]
    public async Task A_build_is_followed_until_it_is_done()
    {
        var runner = new FakeWslcRunner().Answer(@"build --progress plain --tag app:1 C:\src\app", "#1 load\n#2 done\n");
        var client = factory.ClientWith(runner);

        var started = await (await client.PostAsJsonAsync("/api/v1/images/build", new BuildImageRequest(@"C:\src\app", "app:1"))).Content.ReadFromJsonAsync<BuildJob>();
        Assert.NotNull(started);
        BuildJob? job = null;
        for (var i = 0; i < 50 && job?.State is null or "running"; i++)
        {
            job = await client.GetFromJsonAsync<BuildJob>($"/api/v1/images/build/{started.Job}");
            await Task.Delay(20);
        }

        Assert.Equal("done", job?.State);
        Assert.Equal(["#1 load", "#2 done"], job?.Output);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/images/build", new BuildImageRequest(""))).StatusCode);
    }

    [Fact]
    public void Load_reports_each_image_once()
    {
        Assert.Equal(["alpine:latest", "sha256:abc"], ImageArchives.Loaded("Loaded image: alpine:latest\nLoaded image ID: sha256:abc\n", "loaded image: alpine:latest"));
    }
}
