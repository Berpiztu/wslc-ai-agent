using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Host;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Testing;

/// <summary>
/// Settings → Testing, kept in <c>testing.json</c> in the agent's data folder so a
/// switch the user turns on stays on (across reloads and restarts) until turned off.
/// </summary>
public sealed class TestingSettingsStore(IOptions<WslcOptions> options)
    : SavedSettings<TestingSettings>(Path.Combine(options.Value.DataDirectory, "testing.json"), new TestingSettings(false));
