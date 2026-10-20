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

        Assert.StartsWith("1 package with a vulnerability moderate or worse", stopped.Message, StringComparison.Ordinal);
        Assert.Contains("WARNING app:1: flask 3.0.2, moderate: GHSA-test-0001 (CVE-2026-0001), fixed in 3.0.3", lines);
        Assert.Contains("ERROR app:1: flask 3.0.2: 1 vulnerability moderate or worse, the worst moderate; 3.0.3 fixes it.", lines);
        Assert.DoesNotContain(lines, line => line.Contains("requests", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_new_container_of_an_image_built_here_is_refused_and_of_a_pulled_one_only_said()
    {
        var built = Launching(repoDigests: "[]");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => built.GuardLaunchAsync("app:1", CancellationToken.None));
        Assert.Contains("app:1: flask 3.0.2: 1 vulnerability moderate or worse, the worst moderate; 3.0.3 fixes it.", refused.Message);

        var pulled = Launching(repoDigests: """["ghcr.io/x/app@sha256:abc"]""");
        var notes = await pulled.GuardLaunchAsync("app:1", CancellationToken.None);
        Assert.Contains(notes, note => note.Contains("comes from a registry", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_check_marks_the_image_and_its_containers_read_it_by_name_and_tag()
    {
        var marks = new ImageMarks(Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory() }), NullLogger<ImageMarks>.Instance);
        await Launching(repoDigests: "[]", marks).ReportAsync("app:1");

        var mark = marks.Of("app:1");
        Assert.NotNull(mark);
        Assert.Equal("moderate", mark.Worst);
        Assert.True(mark.Refused);

        ContainerSummary Of(string image) => new("c1", "web", image, "running", "Up", "", "", "", [], "", "", "", "");
        var rows = marks.AnnotateContainers([Of("app:1"), Of("app:2")]);
        Assert.Equal("moderate", rows[0].ImageWorst);
        Assert.True(rows[0].ImageRefused);
        Assert.Equal("", rows[1].ImageWorst);

        var images = marks.Annotate([new ImageSummary("2", "app", "1", "app:1", "", "", "", null, true)]);
        Assert.Equal("moderate", Assert.Single(images).ImageWorst);
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

    /// <summary>The check before a container: app:1 on the machine, built here when it has no digest.</summary>
    private static ImageSecurity Launching(string repoDigests, ImageMarks? marks = null)
    {
        var runner = Exporting(Libraries)
            .Answer("image inspect app:1", $$"""[{"Id": "sha256:{{repoDigests.Length}}", "RepoDigests": {{repoDigests}}}]""");
        return new ImageSecurity(runner, new HttpClient(new Osv()), Store(ImageSecurityPolicy.Block), NullLogger<ImageSecurity>.Instance, marks: marks);
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
    public void A_system_package_with_a_fix_is_patched_by_a_patched_tag_that_upgrades_it()
    {
        Installed[] alpine = [new("Alpine:v3.24", "apk-tools", "3.0.8-r0"), new("Alpine:v3.24", "zlib", "1.3.2-r0")];
        ImageVulnerability[] zlib = [new("zlib", "1.3.2-r0", "ALPINE-CVE-2026-85091", "CVE-2026-85091", "high", "1.3.2-r1", "", "Alpine:v3.24")];
        Assert.Equal("alpine:latest-patched", ImagePatches.PatchOf("alpine:latest", alpine, zlib));
        Assert.Equal("alpine:latest-patched", ImagePatches.PatchOf("alpine:latest-patched", alpine, zlib));
        // No package manager (distroless), no fix published, or only a Python library: nothing to patch.
        Assert.Equal("", ImagePatches.PatchOf("alpine:latest", [alpine[1]], zlib));
        Assert.Equal("", ImagePatches.PatchOf("alpine:latest", alpine, [zlib[0] with { FixedIn = "" }]));
        Assert.Equal("", ImagePatches.PatchOf("app:1", alpine, [new("black", "23.1", "PYSEC-1", "", "high", "24.3", "")]));

        Assert.Equal("FROM alpine:latest\nUSER root\nRUN apk upgrade --no-cache\n", ImagePatches.Dockerfile("alpine:latest", "Alpine:v3.24", ""));
        Assert.EndsWith("apt-get upgrade -y && rm -rf /var/lib/apt/lists/*\nUSER app\n", ImagePatches.Dockerfile("web:1", "Debian:12", "app"));
    }

    [Fact]
    public void A_cvss_4_vector_scores_as_nvd_scores_it()
    {
        // Alpine's zlib advisory, which gives only this vector: 8.3 at NVD.
        Assert.Equal(8.3, Cvss4.Score("CVSS:4.0/AV:N/AC:H/AT:N/PR:N/UI:N/VC:N/VI:H/VA:H/SC:N/SI:N/SA:N"));
        Assert.Equal("high", ImageSecurity.CvssSeverity("CVSS:4.0/AV:N/AC:H/AT:N/PR:N/UI:N/VC:N/VI:H/VA:H/SC:N/SI:N/SA:N/E:X/CR:X/IR:X/AR:X"));
        // 5.65 in floating point is 5.6499999: NVD says 5.7.
        Assert.Equal(5.7, Cvss4.Score("CVSS:4.0/AV:L/AC:H/AT:P/PR:H/UI:N/VC:H/VI:L/VA:N/SC:N/SI:N/SA:N"));
        Assert.Equal(10.0, Cvss4.Score("CVSS:4.0/AV:N/AC:L/AT:N/PR:N/UI:N/VC:H/VI:H/VA:H/SC:H/SI:H/SA:H"));
        Assert.Equal("unknown", ImageSecurity.CvssSeverity("CVSS:4.0/AV:N/AC:L/AT:N/PR:N/UI:N/VC:N/VI:N/VA:N/SC:N/SI:N/SA:N"));
        Assert.Null(Cvss4.Score("CVSS:4.0/AV:N/AC:L"));
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
    public void A_library_whose_worst_is_high_or_critical_is_shown_as_an_error()
    {
        ImageVulnerability Found(string severity) => new("pypdf", "4.0.2", "GHSA-x", "", severity, "6.19.0", "");

        Assert.Equal("ERROR", ImageRemedies.LevelOfLine(ImageSecurity.Remedy("img", [Found("high")])));
        Assert.Equal("ERROR", ImageRemedies.LevelOfLine(ImageSecurity.Remedy("img", [Found("critical")])));
        Assert.Equal("WARNING", ImageRemedies.LevelOfLine(ImageSecurity.Remedy("img", [Found("moderate")])));
        Assert.Equal("WARNING", ImageRemedies.LevelOfLine("img comes from a registry and has Python libraries with a vulnerability moderate or worse."));
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

        Assert.Equal([("flask", "3.0.2"), ("rich", "13.7.1")], Named(await ImageSecurity.InstalledAsync(archive, CancellationToken.None)));
    }

    [Fact]
    public async Task An_alpine_image_s_packages_are_read_by_their_origin_in_its_release()
    {
        var archive = Path.Combine(TestHost.TempDataDirectory(), "alpine.tar");
        WriteFiles(archive,
            ("etc/os-release", "NAME=\"Alpine Linux\"
ID=alpine
VERSION_ID=3.18.4
"),
            ("lib/apk/db/installed", "C:Q1
P:libcrypto3
V:3.1.0-r0
o:openssl

P:busybox
V:1.36.0-r0
o:busybox

P:libssl3
V:3.1.0-r0
o:openssl
"));

        var installed = await ImageSecurity.InstalledAsync(archive, CancellationToken.None);

        // libcrypto3 and libssl3 are both openssl, as OSV.dev names them: once.
        Assert.Equal(["Alpine:v3.18 busybox 1.36.0-r0", "Alpine:v3.18 openssl 3.1.0-r0"], installed!.Select(p => $"{p.Ecosystem} {p.Name} {p.Version}"));
    }

    [Fact]
    public async Task A_debian_image_s_installed_packages_are_read_by_their_source()
    {
        var archive = Path.Combine(TestHost.TempDataDirectory(), "debian.tar");
        WriteFiles(archive,
            ("usr/lib/os-release", "ID=debian
VERSION_ID=\"12\"
"),
            ("var/lib/dpkg/status",
                "Package: libssl3
Status: install ok installed
Source: openssl
Version: 3.0.9-1

"
                + "Package: zlib1g
Status: install ok installed
Source: zlib (1:1.2.13.dfsg-1)
Version: 1:1.2.13.dfsg-1+b1

"
                + "Package: oldlib
Status: deinstall ok config-files
Version: 1.0
"));

        var installed = await ImageSecurity.InstalledAsync(archive, CancellationToken.None);

        // By source, at the source's version when it gives one; a package removed but for its configuration is not installed.
        Assert.Equal(["Debian:12 openssl 3.0.9-1", "Debian:12 zlib 1:1.2.13.dfsg-1"], installed!.Select(p => $"{p.Ecosystem} {p.Name} {p.Version}"));
    }

    [Fact]
    public void A_distribution_osv_dev_does_not_know_gives_no_ecosystem()
    {
        Assert.Equal("Ubuntu:22.04", ImageSecurity.Ecosystem(new Dictionary<string, string> { ["ID"] = "ubuntu", ["VERSION_ID"] = "22.04" }));
        Assert.Null(ImageSecurity.Ecosystem(new Dictionary<string, string> { ["ID"] = "kali", ["VERSION_ID"] = "2026.2" }));
        Assert.Null(ImageSecurity.Ecosystem(new Dictionary<string, string> { ["ID"] = "debian" }));
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
        Assert.Equal([("pypdf", "6.21.1")], Named(await ImageSecurity.InstalledAsync(archive, CancellationToken.None)));
    }

    /// <summary>The names and versions read, for the tests of Python libraries.</summary>
    private static List<(string, string)> Named(List<Installed>? installed) => [.. (installed ?? []).Select(p => (p.Name, p.Version))];

    /// <summary>An exported filesystem holding these files and nothing else.</summary>
    private static void WriteFiles(string path, params (string Name, string Text)[] files)
    {
        using var file = File.Create(path);
        using var tar = new TarWriter(file);
        foreach (var (name, text) in files)
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
        }
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
