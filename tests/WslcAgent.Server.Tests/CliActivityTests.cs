using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// How a command line is classified and titled for CLI Activity; the
/// running-then-written life of a command; and the block that carries it
/// through the log file and back.
/// </summary>
public sealed class CliActivityTests
{
    [Fact]
    public void Stats_all_is_container_stats()
    {
        var (kind, title, session) = CliTraceDescription.Describe(
            ["--session", "wslc-cli-alice", "stats", "--all", "--format", "json"]);

        Assert.Equal("containers", kind);
        Assert.Equal("Stats (all containers)", title);
        Assert.Equal("wslc-cli-alice", session);
    }

    [Fact]
    public void Session_equals_and_image_pull()
    {
        var (kind, title, session) = CliTraceDescription.Describe(["--session=alpha", "image", "pull", "alpine:latest"]);

        Assert.Equal("images", kind);
        Assert.Equal("Pull alpine:latest", title);
        Assert.Equal("alpha", session);
    }

    [Fact]
    public void Top_level_pull_and_run()
    {
        var pull = CliTraceDescription.Describe(["pull", "nginx"]);
        Assert.Equal("images", pull.Kind);
        Assert.Equal("Pull nginx", pull.Title);

        var run = CliTraceDescription.Describe(["run", "--name", "web", "alpine"]);
        Assert.Equal("containers", run.Kind);
        Assert.Equal("Run alpine", run.Title);
    }

    [Fact]
    public void List_nouns()
    {
        Assert.Equal("List containers", CliTraceDescription.Describe(["container", "ls"]).Title);
        Assert.Equal("images", CliTraceDescription.Describe(["images"]).Kind);
        Assert.Equal("List images", CliTraceDescription.Describe(["images"]).Title);
        Assert.Equal("networks", CliTraceDescription.Describe(["network", "ls"]).Kind);
        Assert.Equal("Create volume data", CliTraceDescription.Describe(["volume", "create", "data"]).Title);

        var inspect = CliTraceDescription.Describe(["inspect", "d9b2f5e320c0c9744c1a8df8ae1c297d45e303777727b9f6e3cdd36a9bafabc1"]);
        Assert.Equal("containers", inspect.Kind);
        Assert.Equal("Inspect d9b2f5e320c0", inspect.Title);
    }

    [Fact]
    public void System_and_help_are_general()
    {
        var sessions = CliTraceDescription.Describe(["system", "session", "list"]);
        Assert.Equal("general", sessions.Kind);
        Assert.Equal("List sessions", sessions.Title);
        Assert.Equal("Help", CliTraceDescription.Describe(["--help"]).Title);
        Assert.Equal("Tool version", CliTraceDescription.Describe(["system", "version"]).Title);
    }

    [Fact]
    public void A_command_runs_in_memory_and_is_written_to_the_log_when_it_ends()
    {
        var written = new List<(LogLevel Level, string Message)>();
        var activity = new CliActivity(new CapturingLogger(written));

        var id = activity.Start(["image", "pull", "alpine"]);
        var running = Assert.Single(activity.Running());
        Assert.Equal(id, running.Id);
        Assert.Equal("running", running.Status);
        Assert.Null(running.Duration);
        Assert.Equal("Pull alpine", running.Title);
        Assert.Empty(written);

        activity.Finish(id, TimeSpan.FromSeconds(2), 1, "error", "pulled 3 layers", "no space left");

        Assert.Empty(activity.Running());
        var (level, message) = Assert.Single(written);
        Assert.Equal(LogLevel.Error, level);
        Assert.Equal($"wslc image pull alpine\nTrace: {id} | Status: error | Exit code: 1 | Duration: 2000 ms\nOutput:\npulled 3 layers\nErrors:\nno space left", message);
    }

    [Fact]
    public void A_finished_command_that_worked_is_information()
    {
        var written = new List<(LogLevel Level, string Message)>();
        var activity = new CliActivity(new CapturingLogger(written));

        activity.Finish(activity.Start(["container", "list"]), TimeSpan.FromMilliseconds(35), 0, "success", "", "");

        Assert.Equal(LogLevel.Information, Assert.Single(written).Level);
    }

    [Fact]
    public void The_block_reads_back_as_the_command_it_wrote()
    {
        var id = new string('a', 32);
        var block = CliTraceBlock.Write(id, "success", 0, TimeSpan.FromMilliseconds(1500), "{\"a\":1}\n", "");
        var finishedAt = new DateTimeOffset(2026, 9, 19, 12, 0, 1, 500, TimeSpan.FromHours(2));

        var command = CliTraceBlock.Read("wslc --session alpha container list --format json", block.Split('\n'), finishedAt);

        Assert.NotNull(command);
        Assert.Equal(id, command.Id);
        Assert.Equal("success", command.Status);
        Assert.Equal(0, command.ExitCode);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), command.Duration);
        Assert.Equal(finishedAt.AddMilliseconds(-1500), command.StartedAt);
        Assert.Equal("{\"a\":1}", command.Stdout);
        Assert.Equal("", command.Stderr);
        Assert.Equal("containers", command.Kind);
        Assert.Equal("List containers", command.Title);
        Assert.Equal("alpha", command.Session);
    }

    /// <summary>A transfer's commands are named by what they carry, not by the temporary file they copy, and keep that name through the log.</summary>
    [Fact]
    public void A_command_run_under_a_title_keeps_it_in_the_log()
    {
        var activity = new CliActivity(NullLogger<CliActivity>.Instance);
        string[] copy = ["container", "cp", @"C:\Temp\wslc-files-upload-1\wslc-cp-3f9a", "web:/data"];
        string[] rename = ["container", "exec", "web", "mv", "-f", "--", "/data/wslc-cp-3f9a", "/data/app.msi"];
        using (CliTitle.Use("Upload app.msi → web:/data"))
        {
            activity.Start(copy);
            activity.Start(rename);
        }

        var running = activity.Running();
        Assert.Equal("Upload app.msi → web:/data", running[0].Title);
        Assert.Equal("Upload app.msi → web:/data · mv", running[1].Title);

        var block = CliTraceBlock.Write(new string('c', 32), "success", 0, TimeSpan.FromMilliseconds(10), "", "", running[0].Title);
        var command = CliTraceBlock.Read("wslc " + string.Join(' ', copy), block.Split('\n'), DateTimeOffset.Now);

        Assert.NotNull(command);
        Assert.Equal("Upload app.msi → web:/data", command.Title);
        Assert.Equal("transfers", command.Kind);
    }

    [Fact]
    public void Output_is_kept_whole_and_other_details_are_not_a_command()
    {
        var output = "[" + string.Join(',', Enumerable.Range(0, 2000).Select(i => $"{{\"i\":{i}}}")) + "]";
        var block = CliTraceBlock.Write(new string('b', 32), "success", 0, TimeSpan.Zero, output, "");

        Assert.EndsWith("Output:\n" + output, block);
        Assert.Null(CliTraceBlock.Read("wslc image pull alpine (terminal)", [], DateTimeOffset.Now));
        Assert.Null(CliTraceBlock.Read("Application started.", ["   at Frame()"], DateTimeOffset.Now));
        Assert.Null(CliTraceBlock.Read("wslc container list", ["   at Frame()"], DateTimeOffset.Now));
    }

    /// <summary>Keeps what the activity writes, level and formatted message, as the file logger would receive it.</summary>
    private sealed class CapturingLogger(List<(LogLevel Level, string Message)> written) : ILogger<CliActivity>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            written.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
