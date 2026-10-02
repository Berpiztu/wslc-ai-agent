using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Images;

namespace WslcAgent.Server.Tests;

/// <summary>Publishing a version: tagged in the registry and pushed, its upload read from what the push draws.</summary>
public sealed class ImagePublishTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void Push_progress_counts_the_uploaded_part_and_layers_the_registry_had_as_done()
    {
        const string output = "The push refers to repository [ghcr.io/me/app]\n"
            + "aaaaaaaaaaaa: Pushing  5MB/10MB\n"
            + "bbbbbbbbbbbb: Layer already exists\n";

        Assert.Equal((75, "Layer already exists"), PushProgress.Read(output));
        Assert.Equal((100, "Pushed"), PushProgress.Read(output + "aaaaaaaaaaaa: Pushed\n1.4: digest: sha256:0123 size: 528\n"));
        Assert.Equal((0, "Pushing..."), PushProgress.Read(""));
    }

    [Fact]
    public async Task Publish_tags_the_version_and_latest_then_starts_their_pushes()
    {
        var runner = new FakeWslcRunner()
            .Answer("image tag app:dev ghcr.io/me/app:1.4", "")
            .Answer("image tag app:dev ghcr.io/me/app:latest", "");

        var response = await factory.ClientWith(runner).PostAsJsonAsync("/api/v1/images/publish", new PublishImageRequest("app:dev", "GHCR.io/me/app:1.4", Latest: true));
        var started = await response.Content.ReadFromJsonAsync<List<ImagePullState>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["image tag app:dev ghcr.io/me/app:1.4", "image tag app:dev ghcr.io/me/app:latest"], runner.Calls.Select(c => string.Join(' ', c)).Where(c => c.StartsWith("image tag", StringComparison.Ordinal)));
        Assert.Equal(["ghcr.io/me/app:1.4", "ghcr.io/me/app:latest"], started!.Select(s => s.Image));
    }

    [Fact]
    public async Task An_image_update_of_a_container_that_is_not_there_pulls_nothing()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var response = await client.PostAsJsonAsync("/api/v1/containers/ghost/update-image", new UpdateContainerImageRequest("ghcr.io/me/app:1.5"));
        var pulls = await client.GetFromJsonAsync<List<ImagePullState>>("/api/v1/images/pulls");

        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty(pulls!);
    }

    [Fact]
    public async Task Publish_without_a_version_is_refused_before_anything_runs()
    {
        var runner = new FakeWslcRunner();

        var response = await factory.ClientWith(runner).PostAsJsonAsync("/api/v1/images/publish", new PublishImageRequest("app:dev", "ghcr.io/me/app"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(runner.Calls);
    }
}
