using Microsoft.Extensions.Options;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Overview;

/// <summary>
/// The Home dashboard, its landscape and portrait views, kept with the user
/// in the agent's data folder (<c>dashboard-v2.5.json</c>), so every client
/// opens the same one. It keeps the client's text as it was written and reads
/// nothing inside it, and a user who has none yet is given the default the
/// agent ships with (embedded, <c>dashboard-v2.5.default.json</c>), written
/// to their file the first time it is asked for. A development build writes
/// that default back to the repository, so the next installer ships what was
/// designed; a release build writes none (<see cref="ShippedFile"/>).
/// </summary>
public sealed class DashboardV2Store
{
    private readonly ShippedFile _default = new("dashboard-v2.5.default.json", "DashboardV2DefaultSource");
    private readonly string _path;
    private readonly Lock _gate = new();
    private string? _layout;

    public DashboardV2Store(IOptions<WslcOptions> options)
    {
        _path = Path.Combine(options.Value.DataDirectory, "dashboard-v2.5.json");
        _layout = ShippedFile.ReadFile(_path);
    }

    /// <summary>What the user has; the default, kept as theirs, the first time they have none; null when there is no default either.</summary>
    public string? Get()
    {
        lock (_gate)
        {
            if (_layout is null && _default.Read() is { } fallback)
            {
                ShippedFile.WriteFile(_path, fallback);
                _layout = fallback;
            }

            return _layout;
        }
    }

    /// <summary>The default the agent ships; null when there is none.</summary>
    public string? Default() => _default.Read();

    /// <summary>The default written back to the repository; false when this build does not write one (a release build).</summary>
    public bool SetDefault(string layout) => _default.Write(layout);

    /// <summary>What the client wrote, kept as it was written; an empty body forgets it.</summary>
    public void Set(string? layout)
    {
        lock (_gate)
        {
            _layout = string.IsNullOrWhiteSpace(layout) ? null : layout;
            if (_layout is null)
            {
                File.Delete(_path);
            }
            else
            {
                ShippedFile.WriteFile(_path, _layout);
            }
        }
    }
}
