using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Tests;

public sealed class ClientsEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Theory]
    [InlineData("windows", "wslc-ai-client.msi")]
    [InlineData("android", "wslc-ai-client.apk")]
    public async Task Describe_names_the_package_for_each_platform(string platform, string filename)
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var info = await client.GetFromJsonAsync<ClientPackageInfo>($"/api/v1/clients/{platform}");

        Assert.NotNull(info);
        Assert.Equal(platform, info.Platform);
        Assert.Equal(filename, info.Filename);
        // A build machine has the package next to the checkout; a clean one does not.
        Assert.True(info.Available ? info.Error.Length == 0 : info.Error.Length > 0);
    }

    [Fact]
    public async Task Unknown_platform_is_a_bad_request()
    {
        var client = factory.ClientWith(new FakeWslcRunner());

        var response = await client.GetAsync("/api/v1/clients/ios");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// What the update toast shows and what its cross does: the download says
    /// how much has arrived out of how much, and a cancelled one stops there
    /// instead of running to the end and opening an installer nobody asked for
    /// any more.
    /// </summary>
    [Fact]
    public async Task Download_reports_its_progress_and_stops_when_it_is_cancelled()
    {
        var folder = TestHost.TempDataDirectory();
        await File.WriteAllBytesAsync(Path.Combine(folder, "wslc-ai-client.msi"), new byte[8 * 1024 * 1024]);
        var http = factory.Agent(new FakeWslcRunner())
            .WithWebHostBuilder(builder => builder.UseSetting("Wslc:ClientPackagesPath", folder))
            .CreateClient();
        var api = new WslcAgentApi(http);

        var whole = new Recorder();
        var downloaded = Path.Combine(folder, "whole.msi");
        await api.DownloadClientPackageAsync("windows", downloaded, whole);

        Assert.Equal(8 * 1024 * 1024, new FileInfo(downloaded).Length);
        Assert.True(whole.Seen.Count > 2, $"one report is not progress: {whole.Seen.Count}");
        Assert.All(whole.Seen, report => Assert.Equal(8L * 1024 * 1024, report.Total));
        Assert.Equal(0, whole.Seen[0].Percent);
        Assert.Equal(100, whole.Seen[^1].Percent);

        using var cancel = new CancellationTokenSource();
        var half = new Recorder(cancel);
        var abandoned = Path.Combine(folder, "half.msi");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => api.DownloadClientPackageAsync("windows", abandoned, half, cancel.Token));

        Assert.True(new FileInfo(abandoned).Length < 8 * 1024 * 1024, "the download ran to the end anyway");
    }

    /// <summary>Records every report as it happens, and optionally cancels at the first byte.</summary>
    private sealed class Recorder(CancellationTokenSource? cancelAtFirstBytes = null) : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Seen { get; } = [];

        public void Report(DownloadProgress value)
        {
            Seen.Add(value);
            if (value.Received > 0)
            {
                cancelAtFirstBytes?.Cancel();
            }
        }
    }

    [Theory]
    [InlineData("0.1.7", 0, "0.1.6", 9, true)]
    [InlineData("0.1.6", 0, "0.1.6", 9, false)]
    [InlineData("0.1.6", 10, "0.1.6", 9, true)]
    [InlineData("0.2", 0, "0.1.9", 0, true)]
    [InlineData("", 0, "0.1.6", 9, false)]
    public void Package_is_newer_by_version_then_by_build(string version, int build, string installedVersion, int installedBuild, bool expected)
    {
        var package = new ClientPackageInfo("windows", "wslc-ai-client.msi", true, version, build, "");

        Assert.Equal(expected, package.IsNewerThan(installedVersion, installedBuild));
    }
}
