using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Images;

namespace WslcAgent.Server.Tests;

/// <summary>The image and volume menus: files through a helper, push, save, volume inspect and usage.</summary>
public sealed class ImageAndVolumeExtrasTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ContainerList = "container list --all --format json";

    [Fact]
    public async Task Image_files_start_a_sleeping_helper_that_never_pulls()
    {
        var client = factory.ClientWith(new PatternRunner(new FakeWslcRunner(), "container run --detach --name wslc-agent-files-", "0123456789abcdef0123\n"));

        var response = await client.PostAsJsonAsync("/api/v1/images/files-session", new ImageFilesRequest("alpine:latest"));
        var session = await response.Content.ReadFromJsonAsync<FilesSession>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new FilesSession("0123456789ab", "/"), session);
    }

    [Fact]
    public async Task Closing_image_files_refuses_a_container_that_is_not_a_helper()
    {
        var runner = new FakeWslcRunner()
            .Answer("container inspect web --format json", """[{"Id":"abc","Name":"/web","State":{"Status":"running"}}]""");

        var response = await factory.ClientWith(runner).DeleteAsync("/api/v1/images/files-session/web");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("rm"));
    }

    [Fact]
    public async Task Push_passes_all_tags_and_save_puts_a_bare_name_in_downloads()
    {
        var expected = ImageService.SavePath("alpine-latest.tar");
        var runner = new FakeWslcRunner()
            .Answer("image push --all-tags ghcr.io/org/app:1", "")
            .Answer($"image save --output {expected} alpine:latest", "");
        var client = factory.ClientWith(runner);

        var pushed = await client.PostAsJsonAsync("/api/v1/images/push", new PushImageRequest("ghcr.io/org/app:1", AllTags: true));
        var saved = await (await client.PostAsJsonAsync("/api/v1/images/save", new SaveImageRequest("alpine:latest", "alpine-latest.tar"))).Content.ReadFromJsonAsync<SavedImage>();

        Assert.Equal(HttpStatusCode.NoContent, pushed.StatusCode);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "alpine-latest.tar"), saved!.Path);
    }

    [Fact]
    public async Task Volume_users_come_from_inspect_and_skip_containers_that_do_not_mount_it()
    {
        var runner = new FakeWslcRunner()
            .Answer(ContainerList, """
                {"ID":"aaa111","Names":"web","Image":"nginx","State":"running","Mounts":"data"}
                {"ID":"bbb222","Names":"db","Image":"postgres","State":"exited","Mounts":"other"}
                {"ID":"ccc333","Names":"wslc-agent-volfiles-123","Image":"alpine","State":"running","Mounts":"data"}
                """)
            .Answer("container inspect aaa111 --format json",
                """[{"Id":"aaa111","Name":"/web","Config":{"Image":"nginx"},"State":{"Status":"running"},"Mounts":[{"Type":"volume","Name":"data","Destination":"/srv","RW":false}]}]""");

        var users = await factory.ClientWith(runner).GetFromJsonAsync<VolumeUsers>("/api/v1/volumes/data/containers");

        var user = Assert.Single(users!.Containers);
        Assert.Equal(("web", "/srv", false, "running"), (user.Name, user.Destination, user.ReadWrite, user.State));
    }

    /// <summary>A runner that answers any command starting with a prefix (the helper names carry a random part).</summary>
    private sealed class PatternRunner(FakeWslcRunner inner, string prefix, string stdout) : Wslc.IWslcRunner
    {
        public bool SupportsTerminal => false;

        public Task<Wslc.WslcResult> RunAsync(IReadOnlyList<string> args, TimeSpan? timeout = null, CancellationToken cancellationToken = default, string? standardInput = null, string? session = null) =>
            string.Join(' ', args).StartsWith(prefix, StringComparison.Ordinal)
                ? Task.FromResult(new Wslc.WslcResult(args, 0, stdout, "", TimeSpan.Zero))
                : inner.RunAsync(args, timeout, cancellationToken, standardInput, session);

        public Task<Wslc.WslcResult> StreamAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default) =>
            inner.StreamAsync(args, onLine, cancellationToken);

        public Task<int> WatchAsync(IReadOnlyList<string> args, Action<string> onLine, CancellationToken cancellationToken = default) =>
            inner.WatchAsync(args, onLine, cancellationToken);

        public Wslc.WslcCommandLine Resolve(IReadOnlyList<string> args) => inner.Resolve(args);

        public Wslc.IWslcSession StartInteractive(IReadOnlyList<string> args, int columns = 120, int rows = 30) => inner.StartInteractive(args, columns, rows);
    }
}
