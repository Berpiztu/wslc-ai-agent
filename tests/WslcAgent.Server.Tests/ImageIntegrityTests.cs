using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Images;

namespace WslcAgent.Server.Tests;

/// <summary>An image checked against the session's record of its layers: what the script says, read; and the endpoint.</summary>
public sealed class ImageIntegrityTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Whole = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string Damaged = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string Images = """
        [{"ID":"111111111111","Repository":"alpine","Tag":"3.24"},{"ID":"222222222222","Repository":"example/app","Tag":"latest"}]
        """;

    private static readonly string Report =
        $"::BAD {Damaged} aa242af8436d 800423 0\n::BAD {Damaged} b7ac2a05e615 67659 0\n::IMAGE {Whole} 1 0\n::IMAGE {Damaged} 39 2\n::END\n";

    [Fact]
    public void A_report_names_each_image_as_the_list_does_and_puts_the_damaged_first()
    {
        ImageIntegrity.Listed[] listed = [new("111111111111", "alpine:3.24"), new("222222222222", "example/app:latest")];

        var checks = ImageIntegrity.Read(Report, listed)!.Images;

        Assert.Equal(["example/app:latest", "alpine:3.24"], checks.Select(check => check.Image));
        Assert.True(checks[0].IsDamaged);
        Assert.Equal([new DamagedLayer("aa242af8436d", 800423, 0), new DamagedLayer("b7ac2a05e615", 67659, 0)], checks[0].Damaged);
        Assert.Equal(39, checks[0].Layers);
        Assert.Contains("2 of its 39 layers", checks[0].Why());
        Assert.Contains("Remove the image and pull it again", checks[0].Why());
        Assert.False(checks[1].IsDamaged);
    }

    [Fact]
    public void A_store_the_script_does_not_know_and_a_report_cut_short_are_not_read_as_whole()
    {
        Assert.Null(ImageIntegrity.Read("::LAYOUT unknown\n::END\n", []));
        Assert.Null(ImageIntegrity.Read($"::IMAGE {Whole} 1 0\n", []));
    }

    [Fact]
    public async Task The_endpoint_checks_one_image_or_all_and_says_when_one_is_not_there()
    {
        var runner = new FakeWslcRunner()
            .Answer("image list --format json", Images)
            .AnswerWhen(args => args.Take(3).SequenceEqual(["system", "session", "run"]), Report);
        var client = factory.ClientWith(runner);

        var all = await client.GetFromJsonAsync<ImageChecks>("/api/v1/images/check");
        var missing = await client.GetAsync("/api/v1/images/check?image=nothing:here");

        Assert.Equal(2, all!.Images.Count);
        Assert.Equal("example/app:latest", all.Images[0].Image);
        Assert.True(all.Images[0].IsDamaged);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public void The_script_only_reads()
    {
        var text = ImageCheckScript.Text.ReplaceLineEndings("\n");

        // What it removes is its own scratch folder, in the session's memory; nothing of the store.
        Assert.DoesNotContain("rm -rf $root", text);
        Assert.DoesNotContain("rm -rf \"$root", text);
        Assert.DoesNotContain("docker ", text);
    }
}