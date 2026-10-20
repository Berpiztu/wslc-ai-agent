using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Projects;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// A project whose container was taken away around the agent (a script that
/// removed it to run one of its own under the name) is incomplete: a gap that
/// stopped being seen would leave the project looking whole with a service gone.
/// </summary>
public sealed class ProjectGapsTests
{
    [Fact]
    public async Task A_service_of_the_kept_file_with_no_container_marks_every_member_and_its_name_holder()
    {
        var gaps = Gaps();

        var rows = await gaps.AnnotateAsync([Member("getemails"), Loose("shop-backend")], complete: true);

        Assert.Equal(["backend"], rows[0].MissingServices);
        Assert.Null(rows[1].MissingServices);
        // The kept file names backend's container shop-backend: the loose one took its place.
        Assert.Equal("service backend of project shop", rows[1].HoldsNameOf);
    }

    [Fact]
    public async Task A_project_being_saved_or_a_partial_list_has_no_gap()
    {
        var gaps = Gaps();
        Assert.Null((await gaps.AnnotateAsync([Member("getemails")], complete: false))[0].MissingServices);

        gaps.Hold("shop");
        Assert.Null((await gaps.AnnotateAsync([Member("getemails")], complete: true))[0].MissingServices);
        gaps.Release("shop");
        Assert.NotNull((await gaps.AnnotateAsync([Member("getemails")], complete: true))[0].MissingServices);
    }

    private static ProjectGaps Gaps()
    {
        var options = Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory() });
        var folder = Path.Combine(options.Value.DataDirectory, "projects", "shop");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "compose.yaml"), """
            services:
              backend:
                image: img
                container_name: shop-backend
              getemails:
                image: img
            """);
        File.WriteAllText(Path.Combine(folder, "project.json"),
            """{"name": "shop", "file": "", "folder": "", "variables": [], "profiles": [], "savedAt": "2026-10-20T00:00:00Z", "services": ["backend", "getemails"]}""");
        var store = new ProjectStore(options);
        return new ProjectGaps(store, new ProjectDependencies(store, new ProjectService(), new FakeWslcRunner()), NullLogger<ProjectGaps>.Instance);
    }

    private static ContainerSummary Member(string service) =>
        new($"id-{service}", $"shop-{service}", "img", "running", "Up", "", "", "", [], "", "", "", "", Project: "shop", Service: service);

    /// <summary>The one a script made under the service's name: of no project.</summary>
    private static ContainerSummary Loose(string name) =>
        new($"id-loose-{name}", name, "img", "running", "Up", "", "", "", [], "", "", "", "");
}
