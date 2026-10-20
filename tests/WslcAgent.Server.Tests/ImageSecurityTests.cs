using System.Formats.Tar;
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
/// the image's exported files and OSV.dev scripted.
/// </summary>
public sealed class ImageSecurityTests
{
    private static readonly (string Name, string Version)[] Libraries = [("flask", "3.0.2"), ("requests", "2.32.3")];

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
    public async Task A_new_container_of_an_image_built_here_is_refused_and_of_a_pulled_one_only_said()
    {
        var built = Launching(repoDigests: "[]");
        var refused = await Assert.ThrowsAsync<ImageRefusedException>(() => built.GuardLaunchAsync("app:1", CancellationToken.None));
        Assert.Contains("app:1: flask 3.0.2: 1 vulnerability moderate or worse, the worst moderate; 3.0.3 fixes it.", refused.Message);

        var pulled = Launching(repoDigests: """["ghcr.io/x/app@sha256:abc"]""");
        var (notes, worst) = await pulled.GuardLaunchAsync("app:1", CancellationToken.None);
        Assert.Contains(notes, note => note.Contains("comes from a registry", StringComparison.Ordinal));
        Assert.Equal("moderate", worst);
    }

    [Fact]
    public async Task A_container_of_an_image_built_here_is_not_started_one_that_runs_is_left_alone_and_one_pulled_always_starts()
    {
        var refused = await Assert.ThrowsAsync<ImageRefusedException>(() => Launching("[]", "exited").GuardStartAsync("web", CancellationToken.None));
        Assert.Equal("moderate", refused.Worst);
        Assert.Contains("was not started", refused.Message);

        await Launching("[]", "running").GuardStartAsync("web", CancellationToken.None);
        await Assert.ThrowsAsync<ImageRefusedException>(() => Launching("[]", "running").GuardStartAsync("web", CancellationToken.None, restart: true));
        await Launching("""["ghcr.io/x/app@sha256:abc"]""", "exited").GuardStartAsync("web", CancellationToken.None);
    }

    [Fact]
    public async Task The_report_lists_every_library_and_says_a_built_image_is_refused()
    {
        var report = await Launching(repoDigests: "[]").ReportAsync("app:1");

        Assert.True(report.Built);
        Assert.True(report.Refused);
        Assert.Equal(["flask", "requests"], report.Packages.Select(p => p.Name));
        Assert.Equal("GHSA-test-0001", Assert.Single(report.Vulnerabilities).Advisory);

        Assert.False((await Launching(repoDigests: """["ghcr.io/x/app@sha256:abc"]""").ReportAsync("app:1")).Refused);
    }

    /// <summary>The check before a container: app:1 on the machine, built here when it has no digest, and container web of it, in <paramref name="state"/>.</summary>
    private static ImageSecurity Launching(string repoDigests, string state = "exited")
    {
        var runner = Exporting(Libraries)
            .Answer("image inspect app:1", $$"""[{"Id": "sha256:{{repoDigests.Length}}", "RepoDigests": {{repoDigests}}}]""")
            .Answer("container inspect web --format json", $$"""[{"Id": "abc123", "Name": "/web", "Config": {"Image": "app:1"}, "State": {"Status": "{{state}}"}}]""");
        return new ImageSecurity(runner, new HttpClient(new Osv()), Store(ImageSecurityPolicy.Block), NullLogger<ImageSecurity>.Instance);
    }

    [Fact]
    public void An_advisory_with_no_severity_is_scored_by_its_cvss_vector()
    {
        // A PYSEC advisory of black, 9.8, and one 7.5; a scope-changed 6.1; ecdsa's 7.4.
        Assert.Equal("critical", ImageSecurity.CvssSeverity("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:H/I:H/A:H"));
        Assert.Equal("high", ImageSecurity.CvssSeverity("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:H/A:N"));
        Assert.Equal("moderate", ImageSecurity.CvssSeverity("CVSS:3.1/AV:N/AC:L/PR:N/UI:R/S:C/C:L/I:L/A:N"));
        Assert.Equal("high", ImageSecurity.CvssSeverity("CVSS:3.1/AV:N/AC:H/PR:N/UI:N/S:U/C:H/I:H/A:N"));
        Assert.Equal("unknown", ImageSecurity.CvssSeverity("CVSS:3.1/AV:N/AC:L/PR:N/UI:N/S:U/C:N/I:N/A:N"));
        Assert.Equal("unknown", ImageSecurity.CvssSeverity("not a vector"));
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
    public void A_library_whose_worst_is_high_or_critical_is_written_in_the_log_as_an_error()
    {
        ImageVulnerability Found(string severity) => new("pypdf", "4.0.2", "GHSA-x", "", severity, "6.19.0", "");

        Assert.Equal("ERROR", ImageSecurity.LogLevelOf(ImageSecurity.Remedy("img", [Found("high")])));
        Assert.Equal("ERROR", ImageSecurity.LogLevelOf(ImageSecurity.Remedy("img", [Found("critical")])));
        Assert.Equal("WARNING", ImageSecurity.LogLevelOf(ImageSecurity.Remedy("img", [Found("moderate")])));
        Assert.Equal("WARNING", ImageSecurity.LogLevelOf("img comes from a registry and has Python libraries with a vulnerability moderate or worse."));
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
        var security = new ImageSecurity(Exporting(), new HttpClient(new Osv()), store, NullLogger<ImageSecurity>.Instance);
        var lines = new List<string>();

        await security.GuardAsync(["static:1"], (level, text) => lines.Add($"{level} {text}"), CancellationToken.None);

        Assert.Contains(lines, line => line.Contains("has no Python libraries", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_libraries_are_read_from_the_files_never_by_running_the_image()
    {
        var (security, runner) = Make(ImageSecurityPolicy.Warn);

        await security.GuardAsync(["app:1"], (_, _) => { }, CancellationToken.None);

        // Made and exported, never started: no run, no start, no exec of the image.
        Assert.DoesNotContain(runner.Calls, call => call is ["container", "run" or "start" or "exec", ..]);
        Assert.Contains(runner.Calls, call => call is ["container", "export", ..]);
    }

    [Fact]
    public async Task Every_installed_library_is_read_once_from_any_site_packages()
    {
        var archive = Path.Combine(TestHost.TempDataDirectory(), "image.tar");
        WriteArchive(archive, [("flask", "3.0.2")], venv: [("flask", "3.0.2"), ("rich", "13.7.1")]);

        Assert.Equal([("flask", "3.0.2"), ("rich", "13.7.1")], await ImageSecurity.InstalledAsync(archive, CancellationToken.None));
    }

    [Fact]
    public async Task A_library_uv_links_from_its_cache_is_read_through_the_link_and_the_cache_alone_is_not_installed()
    {
        var archive = Path.Combine(TestHost.TempDataDirectory(), "uv.tar");
        using (var file = File.Create(archive))
        using (var tar = new TarWriter(file))
        {
            const string cached = "root/.cache/uv/archive-v0/abc/pypdf-6.21.1.dist-info/METADATA";
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, cached)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("Metadata-Version: 2.1\nName: pypdf\nVersion: 6.21.1\n")),
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "root/.cache/uv/archive-v0/def/black-24.2.0.dist-info/METADATA")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("Metadata-Version: 2.1\nName: black\nVersion: 24.2.0\n")),
            });
            tar.WriteEntry(new PaxTarEntry(TarEntryType.HardLink, "usr/local/lib/python3.12/site-packages/pypdf-6.21.1.dist-info/METADATA") { LinkName = cached });
        }

        // pypdf is installed through its link; black is only in the cache.
        Assert.Equal([("pypdf", "6.21.1")], await ImageSecurity.InstalledAsync(archive, CancellationToken.None));
    }

    /// <summary>wslc as the check asks it: a container of the image made, and its files exported as an archive holding these libraries' metadata.</summary>
    private static FakeWslcRunner Exporting(params (string Name, string Version)[] libraries) =>
        new FakeWslcRunner()
            .AnswerWhen(args => args is ["container", "create", ..], "")
            .AnswerWhen(args => args is ["container", "export", ..], args => WriteArchive(args[args.ToList().IndexOf("--output") + 1], libraries));

    /// <summary>An exported filesystem: the libraries in the image's own site-packages, and others in a virtual environment's.</summary>
    private static void WriteArchive(string path, (string Name, string Version)[] libraries, (string Name, string Version)[]? venv = null)
    {
        using var file = File.Create(path);
        using var tar = new TarWriter(file);
        void Write(string folder, (string Name, string Version) library) =>
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{folder}/{library.Name}-{library.Version}.dist-info/METADATA")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(
                    $"Metadata-Version: 2.1\nName: {library.Name}\nVersion: {library.Version}\n\nVersion: 0.0 in the description is not read.\n")),
            });

        foreach (var library in libraries)
        {
            Write("usr/local/lib/python3.12/site-packages", library);
        }

        foreach (var library in venv ?? [])
        {
            Write("opt/venv/lib/python3.12/site-packages", library);
        }
    }

    private static (ImageSecurity Security, FakeWslcRunner Runner) Make(string policy)
    {
        var runner = Exporting(Libraries);
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
