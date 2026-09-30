using Berpiztu.Dashboard.Model;
using Berpiztu.Dashboard.Storage;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Dashboard;

/// <summary>
/// How each kind of dashboard object is born, view by view, as the agent
/// ships them (<c>/dashboard/object-defaults</c>): read once, and written a
/// kind at a time from the board of every object by a development agent,
/// which writes them back to the repository; a release agent only reads them.
/// </summary>
public sealed class AgentObjectDefaults(WslcAgentApi api) : IObjectDefaults
{
    private readonly Dictionary<string, Dictionary<string, ObjectDefault>> _views = [];
    private Task? _loading;

    public bool Writable { get; private set; }

    public Task LoadAsync(CancellationToken cancellationToken = default) => _loading ??= ReadAsync(cancellationToken);

    public ObjectDefault? For(string view, string type) => _views.GetValueOrDefault(view)?.GetValueOrDefault(type);

    public async Task<bool> SetAsync(string view, string type, ObjectDefault value, CancellationToken cancellationToken = default)
    {
        try
        {
            await api.SetObjectDefaultAsync(view, type, value.Write(), cancellationToken);
        }
        catch (AgentApiException)
        {
            return false;
        }

        if (!_views.TryGetValue(view, out var types))
        {
            _views[view] = types = [];
        }

        types[type] = value;
        return true;
    }

    /// <summary>How many kinds have a default designed in this view.</summary>
    public int Count(string view) => _views.GetValueOrDefault(view)?.Count ?? 0;

    /// <summary>
    /// Every kind's default in one view written as its default in the other
    /// (the objects designed for a phone given to the landscape view, or the
    /// other way), a kind at a time; the number
    /// written, or null where the agent stopped keeping them part of the way.
    /// </summary>
    public async Task<int?> CopyAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        var copied = 0;
        foreach (var (type, value) in _views.GetValueOrDefault(from)?.ToList() ?? [])
        {
            if (!await SetAsync(to, type, value, cancellationToken))
            {
                return null;
            }

            copied++;
        }

        return copied;
    }

    /// <summary>The agent's defaults; an agent that has none, or cannot say, gives none, and every object is born as its descriptor says.</summary>
    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var shipped = await api.GetObjectDefaultsAsync(cancellationToken);
            Writable = shipped.Writable;
            foreach (var (view, types) in ObjectDefault.ReadAll(shipped.Defaults))
            {
                _views[view] = types.ToDictionary();
            }
        }
        catch (AgentApiException)
        {
            Writable = false;
        }
    }
}
