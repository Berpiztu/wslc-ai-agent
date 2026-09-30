using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Images;

namespace WslcAgent.Server.Tests;

/// <summary>Reading a pull's terminal output, and whether a run's image is already local.</summary>
public sealed class ImagePullsTests
{
    [Fact]
    public void Progress_counts_download_as_the_first_half_and_extraction_as_the_second()
    {
        const string output = "latest: Pulling from library/alpine\r\n"
            + "aaaaaaaaaaaa: Downloading  5MB/10MB\r"
            + "bbbbbbbbbbbb: Already exists\r\n";

        var (pct, status) = PullProgress.Read(output);

        Assert.Equal(62, pct);
        Assert.Equal("Already exists", status);
        Assert.Equal((100, "Downloaded"), PullProgress.Read(output + "aaaaaaaaaaaa: Pull complete\nStatus: Downloaded newer image\n"));
    }

    [Fact]
    public void Updates_redrawn_in_place_by_the_console_are_read_one_per_line()
    {
        // What the pseudo console sends: each layer's update after a cursor move, no line break.
        const string output = "aaaaaaaaaaaa: Downloading [>   ] 1MB/10MB[2;1Hbbbbbbbbbbbb: Pull complete[K[1A[32maaaaaaaaaaaa: Download complete[0m";

        var (log, _) = PullProgress.Log(output);

        Assert.Equal("aaaaaaaaaaaa: Downloading [>   ] 1MB/10MB\nbbbbbbbbbbbb: Pull complete\naaaaaaaaaaaa: Download complete", log);
        Assert.Equal((75, "Download complete"), PullProgress.Read(output));
    }

    [Fact]
    public void Log_drops_terminal_codes_and_blank_lines_and_failure_names_the_denial()
    {
        var (log, truncated) = PullProgress.Log("[2K[1Gline one\r\n\r\n[32mline two[0m");

        Assert.Equal("line one\nline two", log);
        Assert.False(truncated);
        Assert.Equal("Pulling\nError: pull access denied for x\nerror code: 0x1", PullProgress.Failure("Pulling\nError: pull access denied for x\nerror code: 0x1\n"));
    }

    /// <summary>
    /// What the CLI printed when the VM had no route out is the message, whole
    /// and as printed: picking one line out of it showed the closing "If this
    /// error was unexpected…" for a network that was unreachable. The layers'
    /// progress lines are the only thing left out.
    /// </summary>
    [Fact]
    public void Failure_is_what_the_cli_said_whole_without_the_layer_progress()
    {
        const string output = "Using default tag: latest\r\n"
            + "Get \"https://ghcr.io/v2/\": dial tcp 140.82.114.33:443: connect: network is unreachable\r\n"
            + "Error code: E_FAIL\r\n"
            + "If this error was unexpected, please consider searching for existing issues or filing a new issue at https://github.com/microsoft/WSL/issues.\r\n";

        Assert.Equal(
            "Using default tag: latest\n"
            + "Get \"https://ghcr.io/v2/\": dial tcp 140.82.114.33:443: connect: network is unreachable\n"
            + "Error code: E_FAIL\n"
            + "If this error was unexpected, please consider searching for existing issues or filing a new issue at https://github.com/microsoft/WSL/issues.",
            PullProgress.Failure(output));
        Assert.Equal("latest: Pulling from library/alpine\nError code: E_FAIL", PullProgress.Failure("latest: Pulling from library/alpine\naaaaaaaaaaaa: Downloading  5MB/10MB\rError code: E_FAIL\n"));
        Assert.Null(PullProgress.Failure("aaaaaaaaaaaa: Downloading  5MB/10MB\r"));
    }

    [Fact]
    public void An_image_is_local_by_reference_or_by_repository_with_latest()
    {
        ImageSummary[] catalog = [new("1", "alpine", "latest", "alpine:latest", "", "", "", null, false), new("2", "ghcr.io/o/app", "main", "ghcr.io/o/app:main", "", "", "", null, false)];

        Assert.True(ContainerLaunches.IsLocal(catalog, "alpine"));
        Assert.True(ContainerLaunches.IsLocal(catalog, "ghcr.io/o/app:main"));
        Assert.False(ContainerLaunches.IsLocal(catalog, "ghcr.io/o/app"));
        Assert.False(ContainerLaunches.IsLocal(catalog, "nginx"));
    }
}
