using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

public sealed class ImagesEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Usage = "container list --all --format json";

    [Fact]
    public async Task List_maps_rows_marks_usage_and_totals_the_size()
    {
        var runner = new FakeWslcRunner()
            .Answer("image list --digests --format json", FakeWslcRunner.Fixture("image-list.ndjson"))
            .Answer(Usage, FakeWslcRunner.Fixture("container-list-usage.ndjson"))
            .Answer("container stats --all --format json", "");

        var body = await factory.ClientWith(runner).GetFromJsonAsync<ImageListResponse>("/api/v1/images");

        Assert.NotNull(body);
        Assert.Equal(4, body.Aggregate.Count);
        Assert.True(body.Aggregate.SizeBytes > 14_000_000_000);
        var webui = body.Images[0];
        Assert.Equal("ghcr.io/open-webui/open-webui:main", webui.Reference);
        Assert.Equal(0, webui.Containers);
        Assert.False(webui.InUse);
        Assert.True(body.Images[1].InUse);
        var dangling = body.Images[3];
        Assert.True(dangling.IsDangling);
        Assert.Equal("0123456789ab", dangling.Id);
        Assert.Equal("0123456789ab", dangling.Reference);
        Assert.Null(dangling.Containers);
        Assert.False(dangling.InUse);
    }

    [Fact]
    public void An_image_whose_tag_a_pull_moved_is_named_by_its_id_not_its_repository()
    {
        // The repository alone is repository:latest to wslc — the new image.
        var row = System.Text.Json.JsonDocument.Parse("""{"ID":"63ca78027b1a","Repository":"ghcr.io/openclaw/openclaw","Tag":"<none>","Containers":"0"}""").RootElement;

        var image = Images.ImageService.ToSummary(row, Containers.ContainerUsage.Empty);

        Assert.True(image.IsDangling);
        Assert.Equal("63ca78027b1a", image.Reference);
    }

    [Fact]
    public async Task Pull_and_tag_run_the_matching_commands()
    {
        var runner = new FakeWslcRunner()
            .Answer("image pull alpine:3.20", "")
            .Answer("image tag alpine:3.20 mine:latest", "");
        var client = factory.ClientWith(runner);

        var pull = await client.PostAsJsonAsync("/api/v1/images/pull", new PullImageRequest("alpine:3.20"));
        var tag = await client.PostAsJsonAsync("/api/v1/images/tag", new TagImageRequest("alpine:3.20", "mine:latest"));

        Assert.Equal(HttpStatusCode.NoContent, pull.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, tag.StatusCode);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task Delete_takes_the_reference_from_the_query_and_honours_force()
    {
        var runner = new FakeWslcRunner().Answer("image remove --force ghcr.io/org/app:tag", "");

        var response = await factory.ClientWith(runner).DeleteAsync("/api/v1/images?reference=ghcr.io%2Forg%2Fapp%3Atag&force=true");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["image", "remove", "--force", "ghcr.io/org/app:tag"], runner.Calls.Single());
    }

    /// <summary>The cross of a failed pull: forgetting one the agent no longer has is not an error, the row is gone either way.</summary>
    [Fact]
    public async Task Dismissing_a_pull_that_is_gone_answers_no_content()
    {
        var response = await factory.ClientWith(new FakeWslcRunner()).DeleteAsync("/api/v1/images/pulls?image=ghcr.io%2Forg%2Fapp%3Alatest");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Prune_removes_dangling_images_only()
    {
        var runner = new FakeWslcRunner().Answer("image prune -f", "");

        var response = await factory.ClientWith(runner).PostAsync("/api/v1/images/prune", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["image", "prune", "-f"], runner.Calls.Single());
    }
}
