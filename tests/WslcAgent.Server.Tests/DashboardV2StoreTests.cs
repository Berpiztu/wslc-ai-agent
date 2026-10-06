using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WslcAgent.Server.Overview;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Tests;

/// <summary>The user's dashboard is one file, whichever agent on the machine serves it.</summary>
public sealed class DashboardV2StoreTests
{
    /// <summary>
    /// An installed agent and a development one share the data folder: each
    /// kept the dashboard it read at its start, so a view saved through one was
    /// not shown by the other, and a save through that one wrote it away.
    /// </summary>
    [Fact]
    public void Two_agents_on_one_data_folder_serve_and_keep_each_others_saves()
    {
        var options = Options.Create(new WslcOptions { DataDirectory = TestHost.TempDataDirectory() });
        var installed = Store(options);
        var development = Store(options);
        installed.Set("""{"columns":24,"objects":[]}""");
        Assert.Equal("""{"columns":24,"objects":[]}""", development.Get());

        development.Set("""{"columns":24,"objects":[],"portrait":{"columns":24,"objects":[]}}""");

        Assert.Equal("""{"columns":24,"objects":[],"portrait":{"columns":24,"objects":[]}}""", installed.Get());
    }

    private static DashboardV2Store Store(IOptions<WslcOptions> options) =>
        new(options, new LoadedDefaults(options), new DefaultsVersions(), new DashboardHistory(options), NullLogger<DashboardV2Store>.Instance);
}
