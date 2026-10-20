using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Images;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>
/// The check after a build: a check that stopped blocking would go unnoticed
/// until a vulnerable library ran, so the blocking is what is tested, with
/// <c>pip list</c> and OSV.dev scripted.
/// </summary>
public sealed class ImageSecurityTests
{
    private const string Pip = """[{"name": "flask", "version": "3.0.2"}, {"name": "requests", "version": "2.32.3"}]""";

    [Fact]
    public async Task A_moderate_vulnerability_stops_the_build_naming_the_version_that_fixes_it()
    {
        var (security, _) = Make(ImageSecurityPolicy.Block);
        var lines = new List<string>();

        var stopped = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            security.GuardAsync(["app:1"], (level, text) => lines.Add($"{level} {text}"), CancellationToken.None));

        Assert.StartsWith("1 Python library with a vulnerability moderate or worse", stopped.Message, StringComparison.Ordinal);
        Assert.Contains("WARNING app:1: flask 3.0.2, moderate: GHSA-test-0001 (CVE-2026-0001), fixed in 3.0.3", lines);
        Assert.Contains("ERROR app:1: flask 3.0.2: 1 vulnerability moderate or worse, the worst moderate; 3.0.3 fixes it.", lines);
        Assert.DoesNotContain(lines, line => line.Contains("requests", StringComparison.Ordinal));
    }

    [Fact]
    public void A_library_is_raised_to_its_highest_fix_or_replaced_when_one_has_none()
    {
        ImageVulnerability Found(string severity, string fixedIn, string advisory = "GHSA-x") =>
            new("pypdf", "4.0.2", advisory, "", severity, fixedIn, "");

        Assert.Equal("img: pypdf 4.0.2: 3 vulnerabilities moderate or worse, the worst high; 6.19.0 fixes them all.",
            ImageSecurity.Remedy("img", [Found("moderate", "6.9.2"), Found("high", "6.19.0"), Found("moderate", "6.10.0")]));
        Assert.Equal("img: pypdf 4.0.2: 2 vulnerabilities moderate or worse, the worst critical; no version fixes GHSA-none yet: replace it.",
            ImageSecurity.Remedy("img", [Found("moderate", "6.9.2"), Found("critical", "", "GHSA-none")]));
    }

    [Fact]
    public async Task Warn_says_it_and_builds_on_and_off_checks_nothing()
    {
        var (warned, _) = Make(ImageSecurityPolicy.Warn);
        var lines = new List<string>();
        await warned.GuardAsync(["app:1"], (level, text) => lines.Add($"{level} {text}"), CancellationToken.None);
        Assert.Contains(lines, line => line.Contains("built anyway", StringComparison.Ordinal));

        var (off, runner) = Make(ImageSecurityPolicy.Off);
        await off.GuardAsync(["app:1"], (_, _) => { }, CancellationToken.None);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task An_image_with_no_python_has_nothing_to_check()
    {
        var store = Store(ImageSecurityPolicy.Block);
        var security = new ImageSecurity(new FakeWslcRunner(), new HttpClient(new Osv()), store, NullLogger<ImageSecurity>.Instance);
        var lines = new List<string>();

        await security.GuardAsync(["static:1"], (level, text) => lines.Add($"{level} {text}"), CancellationToken.None);

        Assert.Contains(lines, line => line.Contains("has no Python with pip", StringComparison.Ordinal));
    }

    private static (ImageSecurity Security, FakeWslcRunner Runner) Make(string policy)
    {
        var runner = new FakeWslcRunner().AnswerWhen(args => args.Contains("pip"), Pip);
        return (new ImageSecurity(runner, new HttpClient(new Osv()), Store(policy), NullLogger<ImageSecurity>.Instance), runner);
    }

    private static ImageSecuritySettingsStore Store(string policy)
    {
        var store = new ImageSecuritySettingsStore(Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory() }));
        store.Set(new ImageSecuritySettings(policy));
        return store;
    }

    /// <summary>OSV.dev as it answers: flask 3.0.2 has one moderate advisory, fixed in 3.0.3; requests has none.</summary>
    private sealed class Osv : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/v1/querybatch" => """{"results": [{"vulns": [{"id": "GHSA-test-0001"}]}, {}]}""",
                "/v1/vulns/GHSA-test-0001" => """
                    {"id": "GHSA-test-0001", "aliases": ["CVE-2026-0001"], "database_specific": {"severity": "MODERATE"},
                     "affected": [{"package": {"name": "Flask", "ecosystem": "PyPI"},
                                   "ranges": [{"type": "ECOSYSTEM", "events": [{"introduced": "0"}, {"fixed": "3.0.3"}]}]}]}
                    """,
                _ => null,
            };
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
