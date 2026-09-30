using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.Server.AgentLog;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The agent's log file in its format, read back, merged and trimmed; the wslc commands it records read back as commands.</summary>
public sealed class AgentLogsTests
{
    [Fact]
    public void A_command_entry_is_read_as_the_command_and_a_running_one_is_listed_after_the_file()
    {
        using var file = NewFile(out var directory);
        var id = new string('c', 32);
        File.WriteAllText(Path.Combine(directory, "wslc-ai-agent.1.log"),
            "2026-09-16 10:00:01.000 | INFO     | Api - started\n"
            + $"2026-09-16 10:00:02.500 | WARNING  | WslcAgent.Server.Wslc.CliActivity - wslc container stop web\nTrace: {id} | Status: error | Exit code: 1 | Duration: 500 ms\nErrors:\nObject not found: web\n");
        var activity = new CliActivity(NullLogger<CliActivity>.Instance);
        var runningId = activity.Start(["image", "pull", "alpine"]);
        var logs = new AgentLogs(file, activity);

        var entries = logs.Read(tail: null);

        Assert.Equal(3, entries.Count);
        Assert.Null(entries[0].Command);
        var stop = entries[1];
        Assert.Equal(1, stop.EntryId);
        Assert.Equal("containers", stop.Kind);
        Assert.Equal(id, stop.Key);
        Assert.NotNull(stop.Command);
        Assert.Equal("error", stop.Command.Status);
        // A failed command is an error whatever its line says (this one was written as WARNING).
        Assert.Equal("ERROR", stop.Level);
        // The list is abridged: the outcome line stays, the output and its section go; the entry whole has them.
        Assert.Equal("", stop.Command.Stderr);
        Assert.Equal([$"Trace: {id} | Status: error | Exit code: 1 | Duration: 500 ms"], stop.Trace);
        var whole = Assert.Single(logs.Entries([1]));
        Assert.Equal("Object not found: web", whole.Command!.Stderr);
        Assert.Equal([$"Trace: {id} | Status: error | Exit code: 1 | Duration: 500 ms", "Errors:", "Object not found: web"], whole.Trace);
        Assert.Equal(new DateTime(2026, 9, 16, 10, 0, 2, 0), stop.Command.StartedAt.LocalDateTime);
        var running = entries[2];
        Assert.True(running.Running);
        Assert.Null(running.EntryId);
        Assert.Equal(AgentLogs.RunningLevel, running.Level);
        Assert.Equal(runningId, running.Key);
        Assert.Equal("wslc image pull alpine", running.Message);
        Assert.Equal("images", running.Kind);

        Assert.Equal([runningId, id], logs.Recent(10).Select(command => command.Id));
        Assert.Equal([runningId], logs.Recent(1).Select(command => command.Id));
        Assert.DoesNotContain("RUNNING", logs.Text(null));

        // After an id: what follows it, the running ones after, whatever the tail says; an id that is gone answers with the tail.
        Assert.Equal([1, null], logs.Read(tail: 1, after: 0).Select(entry => entry.EntryId));
        Assert.Equal([null], logs.Read(tail: 1, after: 1).Select(entry => entry.EntryId));
        Assert.Equal([1, null], logs.Read(tail: 1, after: 999).Select(entry => entry.EntryId));
    }

    [Fact]
    public void Entries_are_written_as_timestamp_level_category_and_message()
    {
        var line = AgentFileLoggerProvider.Format(new DateTime(2026, 9, 16, 10, 11, 12, 345), LogLevel.Warning, "WslcAgent.Server.Wslc.WslcRunner", "one\r\ntwo", null);

        Assert.Equal("2026-09-16 10:11:12.345 | WARNING  | WslcAgent.Server.Wslc.WslcRunner - one\ntwo\n", line);
    }

    [Fact]
    public void Lines_after_an_entry_are_its_details_and_files_merge_by_time()
    {
        using var file = NewFile(out var directory);
        File.WriteAllText(Path.Combine(directory, "wslc-ai-agent.1.log"),
            "2026-09-16 10:00:02.000 | ERROR    | Api - failed\n   at Frame()\n");
        File.WriteAllText(Path.Combine(directory, "wslc-ai-agent.2.log"),
            "2026-09-16 10:00:01.000 | INFO     | Api - started\n");
        var logs = Logs(file);

        var entries = logs.Read(tail: null);

        Assert.Equal(["started", "failed"], entries.Select(e => e.Message));
        // An id is the file and the line, not the place in the merged stream: the second file's first line, the first file's first line.
        Assert.Equal([AgentLogs.IdsPerFile, 0], entries.Select(e => e.EntryId));
        Assert.Equal("2026-09-16 10:00:02.0", entries[1].DisplayTimestamp);
        Assert.Equal(["Timestamp: 2026-09-16 10:00:02.000", "Level: ERROR", "Source: Api", "Message: failed", "", "   at Frame()"], entries[1].Details);
        Assert.Single(logs.Read(tail: 1));
    }

    [Fact]
    public void Delete_rewrites_the_file_without_the_chosen_entries()
    {
        using var file = NewFile(out var directory);
        var path = Path.Combine(directory, "wslc-ai-agent.1.log");
        File.WriteAllText(path,
            "2026-09-16 10:00:01.000 | INFO     | Api - keep\n2026-09-16 10:00:02.000 | ERROR    | Api - drop\n   detail\n");
        var logs = Logs(file);

        Assert.Equal(1, logs.Delete([1]));

        Assert.Equal("2026-09-16 10:00:01.000 | INFO     | Api - keep\n", File.ReadAllText(path));
        Assert.Equal("2026-09-16 10:00:01.000 | INFO     | Api - keep", logs.Text(0));
    }

    [Fact]
    public void Kind_is_the_resource_of_a_logged_wslc_command()
    {
        Assert.Equal("containers", AgentLogs.KindOf("wslc --session wslc-cli-me container list --all --format json"));
        Assert.Equal("images", AgentLogs.KindOf("wslc image pull alpine"));
        Assert.Equal("general", AgentLogs.KindOf("wslc system info --format json"));
        Assert.Equal("general", AgentLogs.KindOf("Server (wslc-ai-agent 0.1.40) method 'ping' request handler called."));
    }

    /// <summary>A transfer is followed through the log on its own: the copy either way, and its steps on the names it travels under; a copy made inside a container is the container's.</summary>
    [Fact]
    public void A_files_way_in_or_out_of_a_container_is_a_file_transfer()
    {
        Assert.Equal("transfers", AgentLogs.KindOf(@"wslc container cp C:\Temp\wslc-files-upload-1\app.msi web:/data"));
        Assert.Equal("transfers", AgentLogs.KindOf("wslc container cp --follow-link web:/data/app.msi C:\\Temp\\app.msi"));
        Assert.Equal("transfers", AgentLogs.KindOf("wslc container exec web mv -f -- /data/wslc-cp-0a1b /data/app.msi"));
        Assert.Equal("containers", AgentLogs.KindOf("wslc container exec web cp -a -- /data/a.txt /data/b.txt"));
        Assert.Equal("transfers", AgentLogs.KindOf("transfer: app.msi → web:/data started"));
    }

    [Fact]
    public void Clear_empties_every_file()
    {
        using var file = NewFile(out var directory);
        var first = Path.Combine(directory, "wslc-ai-agent.1.log");
        var second = Path.Combine(directory, "wslc-ai-agent.2.log");
        File.WriteAllText(first, "2026-09-16 10:00:01.000 | INFO     | Api - one\n   detail\n");
        File.WriteAllText(second, "2026-09-16 10:00:02.000 | ERROR    | Api - two\n2026-09-16 10:00:03.000 | INFO     | Api - three\n");
        var logs = Logs(file);

        Assert.Equal(3, logs.Clear());

        Assert.Equal("", File.ReadAllText(first));
        Assert.Equal("", File.ReadAllText(second));
        Assert.Empty(logs.Read(tail: null));
        Assert.Equal(0, logs.Clear());
    }

    [Fact]
    public void Tail_defaults_to_500_and_stops_at_5000()
    {
        Assert.Equal(500, AgentLogs.NormalizeTail(null));
        Assert.Equal(500, AgentLogs.NormalizeTail(0));
        Assert.Equal(5000, AgentLogs.NormalizeTail(90000));
        Assert.Equal(20, AgentLogs.NormalizeTail(20));
    }

    private static AgentLogFile NewFile(out string directory)
    {
        directory = TestHost.TempDataDirectory();
        return new AgentLogFile(Options.Create(new WslcOptions { LogDirectory = directory }));
    }

    /// <summary>The reader over a file with nothing running.</summary>
    private static AgentLogs Logs(AgentLogFile file) => new(file, new CliActivity(NullLogger<CliActivity>.Instance));
}
