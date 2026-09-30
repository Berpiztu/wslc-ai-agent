using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Containers;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// A file on its way in or out of a container: the agent counts what it
/// carries and keeps the job, so the view that started the transfer can close
/// and the container's row still knows what is happening to it.
/// </summary>
public sealed class ContainerTransferTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task An_upload_is_a_job_the_agent_counted_and_still_lists_when_it_is_done()
    {
        // The staged path carries a guid, so the copy is matched by its shape.
        var runner = new FakeWslcRunner()
            .AnswerWhen(args => args is ["container", "cp", _, "web:/app"], "")
            .Answer("exec web ls -lad -- /app/app.tar", "-rw-r--r-- 1 root root 5 Sep 15 08:55 /app/app.tar");
        var client = factory.ClientWith(runner);
        using var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "app.tar" } };

        var response = await client.PostAsync("/api/v1/containers/web/files/upload?path=/app&size=5", form);
        var transfers = await client.GetFromJsonAsync<IReadOnlyList<ContainerTransfer>>("/api/v1/containers/transfers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("web:/app", runner.Calls.Single(call => call is ["container", "cp", ..])[3]);
        Assert.NotNull(transfers);
        var upload = Assert.Single(transfers);
        Assert.Equal(("web", ContainerTransfer.In, "/app", "app.tar"), (upload.Container, upload.Direction, upload.Directory, upload.Name));
        Assert.Equal(("done", 100, 5L), (upload.Phase, upload.Pct, upload.Bytes));
        Assert.False(upload.Active);
    }

    /// <summary>The download carries the file itself, and the job ends with the response that sent it.</summary>
    [Fact]
    public async Task A_download_hands_over_the_file_and_the_job_says_it_went_whole()
    {
        var runner = new FakeWslcRunner()
            .Answer("exec web ls -lad -- /app/app.tar", "-rw-r--r-- 1 root root 5 Sep 15 08:55 /app/app.tar")
            // The copy out of the container is what leaves the staged file there.
            .AnswerWhen(args => args is ["container", "cp", "--follow-link", "web:/app/app.tar", _], args => File.WriteAllText(args[4], "hello"));
        var client = factory.ClientWith(runner);

        var response = await client.GetAsync("/api/v1/containers/web/files/download?path=/app/app.tar");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var transfers = await client.GetFromJsonAsync<IReadOnlyList<ContainerTransfer>>("/api/v1/containers/transfers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(bytes));
        Assert.Equal("app.tar", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        Assert.NotNull(transfers);
        var download = Assert.Single(transfers);
        Assert.Equal((ContainerTransfer.Out, "/app", "app.tar", "done"), (download.Direction, download.Directory, download.Name, download.Phase));
        Assert.Equal(100, download.Pct);
    }

    [Fact]
    public async Task A_body_with_no_file_in_it_is_refused_before_anything_is_staged()
    {
        var runner = new FakeWslcRunner();
        var client = factory.ClientWith(runner);
        using var form = new MultipartFormDataContent { { new StringContent("/app"), "path" } };

        var response = await client.PostAsync("/api/v1/containers/web/files/upload?path=/app", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(runner.Calls);
    }

    /// <summary>Going in, the ring is the bytes received; the copy into the container has no measure and holds it at the top.</summary>
    [Fact]
    public void An_uploads_percentage_is_what_has_arrived_of_what_was_announced()
    {
        var transfers = new ContainerTransfers(new WslcEvents());
        var upload = transfers.Start("web", ContainerTransfer.In, "/app", "app.tar", 1000, CancellationToken.None);

        upload.Progress(250);
        Assert.Equal((25, ContainerTransfer.Receiving), (upload.Snapshot().Pct, upload.Snapshot().Phase));

        upload.Copying();
        var copying = upload.Snapshot();
        Assert.Equal((100, ContainerTransfer.Copying), (copying.Pct, copying.Phase));
        Assert.Contains("Copying", copying.Status, StringComparison.Ordinal);
    }

    /// <summary>Coming out, the two halves of the ring are the copy out of the container and the send to the client, as a pull's are.</summary>
    [Fact]
    public void A_downloads_two_stages_are_halves_of_the_ring()
    {
        var transfers = new ContainerTransfers(new WslcEvents());
        var download = transfers.Start("web", ContainerTransfer.Out, "/app", "app.tar", 0, CancellationToken.None);

        download.Total(1000);
        download.Progress(500);
        Assert.Equal((25, ContainerTransfer.Copying), (download.Snapshot().Pct, download.Snapshot().Phase));

        download.Sending();
        download.Progress(500);
        Assert.Equal((75, ContainerTransfer.Sending), (download.Snapshot().Pct, download.Snapshot().Phase));

        download.Finish();
        Assert.Equal((100, "done"), (download.Snapshot().Pct, download.Snapshot().Phase));
    }

    /// <summary>
    /// The next file's turn waits for the one travelling: it was given the turn
    /// the moment the first started, its minute ran out while an installer was
    /// still going up, and the rest of the batch was dropped without a word.
    /// </summary>
    [Fact]
    public void The_next_file_has_its_turn_only_once_the_one_travelling_has_ended()
    {
        var transfers = new ContainerTransfers(new WslcEvents());
        var batch = transfers.Announce("web", ContainerTransfer.In, "/app", [new AnnouncedFile("agent.msi", 1000), new AnnouncedFile("client.apk", 1000)]);
        var (first, second) = (batch.Files[0].Id, batch.Files[1].Id);

        var travelling = transfers.Claim(first, CancellationToken.None);

        Assert.Equal(1, transfers.List().Single(transfer => transfer.Id == second).Position);
        Assert.Throws<InvalidOperationException>(() => transfers.Claim(second, CancellationToken.None));

        travelling.Finish();

        Assert.Equal(0, transfers.List().Single(transfer => transfer.Id == second).Position);
        Assert.Equal(ContainerTransfer.Receiving, transfers.Claim(second, CancellationToken.None).Snapshot().Phase);
    }

    /// <summary>Each file is a row of CLI Activity from its announcement to its end, so one that started and never finished shows.</summary>
    [Fact]
    public void Every_file_announced_is_a_running_activity_until_it_ends()
    {
        var activity = new CliActivity(Microsoft.Extensions.Logging.Abstractions.NullLogger<CliActivity>.Instance);
        var transfers = new ContainerTransfers(new WslcEvents(), activity: activity);
        var batch = transfers.Announce("web", ContainerTransfer.In, "/data", [new AnnouncedFile("agent.msi", 1000), new AnnouncedFile("client.apk", 1000)]);

        var rows = activity.Running();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal(("transfers", "transfer"), (row.Kind, row.Program)));
        Assert.Equal("web · Upload · agent.msi → /data", rows[0].Title);

        transfers.Claim(batch.Files[0].Id, CancellationToken.None).Finish();

        Assert.Equal("web · Upload · client.apk → /data", Assert.Single(activity.Running()).Title);
    }

    /// <summary>Cancelling pulls the token every step of the transfer runs under, and the job says who ended it.</summary>
    [Fact]
    public void Cancelling_stops_the_transfer_and_dismissing_it_early_is_refused()
    {
        var transfers = new ContainerTransfers(new WslcEvents());
        var upload = transfers.Start("web", ContainerTransfer.In, "/app", "app.tar", 1000, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => transfers.Dismiss(upload.Id));

        transfers.Cancel(upload.Id);
        Assert.True(upload.Token.IsCancellationRequested);

        // The request fails where it was: what the row shows is that it was stopped, not that it broke.
        upload.Fail("The operation was canceled.");
        Assert.Equal("cancelled", upload.Snapshot().Phase);
        Assert.Equal("", upload.Snapshot().Error);

        transfers.Dismiss(upload.Id);
        Assert.Empty(transfers.List());
    }
}
