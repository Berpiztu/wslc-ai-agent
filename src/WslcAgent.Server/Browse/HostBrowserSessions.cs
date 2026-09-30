using Microsoft.Extensions.Options;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Browse;

/// <summary>
/// Every live host browser on the agent: found by its key (client, viewer,
/// container, port) to resume, or by its id to join; capped in number, and
/// closed by <see cref="ReapAsync"/> when nobody has watched one for too long.
/// </summary>
public sealed class HostBrowserSessions(
    IBrowserPageFactory pages,
    IOptionsMonitor<BrowseOptions> options,
    TimeProvider clock,
    ILogger<HostBrowserSessions> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, HostBrowserSession> _byKey = [];
    private readonly Dictionary<string, HostBrowserSession> _byId = [];

    /// <summary>A pane attached to a session: the session, the pane's viewer token, and whether the browser was just launched.</summary>
    public sealed record Attached(HostBrowserSession Session, int Viewer, bool Created);

    public int LiveCount
    {
        get
        {
            lock (_byId)
            {
                return _byId.Values.Count(s => s.IsAlive);
            }
        }
    }

    /// <summary>Resumes the live browser under <paramref name="key"/>, or launches one at <paramref name="url"/> when there is room.</summary>
    public async Task<Attached> OpenAsync(string key, string container, string client, string viewerId, string url, int width, int height, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(_byKey, key) is { } existing)
            {
                if (existing.IsAlive)
                {
                    var viewer = existing.BeginView();
                    try
                    {
                        await existing.RefitAsync(width, height);
                        return new Attached(existing, viewer, Created: false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning("Browse resume failed key={Key}: {Message}", key, ex.Message);
                    }
                }

                Forget(existing);
                await existing.DisposeAsync();
            }

            var cap = options.CurrentValue.MaxSessions;
            var live = LiveCount;
            if (cap > 0 && live >= cap)
            {
                throw new BrowseException($"Too many host browser sessions open ({live}/{cap}). Disconnect one from View browser sessions first.");
            }

            var session = new HostBrowserSession(pages.Create(), clock)
            {
                ContainerId = BrowseTarget.NormalizeContainerId(container),
                Client = client,
                OwnerViewerId = viewerId,
                RegistryKey = key,
            };
            int token;
            try
            {
                token = await session.StartAsync(url, width, height, cancellationToken);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }

            lock (_byId)
            {
                _byKey[key] = session;
                _byId[session.Id] = session;
            }

            return new Attached(session, token, Created: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Joins another client's browser as an extra pane (View browser sessions → Connect).</summary>
    public async Task<Attached> JoinAsync(string sessionId, string container, int width, int height, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(_byId, sessionId.Trim()) is not { IsAlive: true } session)
            {
                throw new BrowseException("Browser session not found");
            }

            if (!BrowseTarget.SameContainer(session.ContainerId, container))
            {
                throw new BrowseException("Session belongs to another container");
            }

            var viewer = session.BeginView();
            await session.EnsureScreencastAsync(width, height);
            return new Attached(session, viewer, Created: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Closes the browser for everyone attached to it.</summary>
    public async Task DestroyAsync(HostBrowserSession session)
    {
        await _gate.WaitAsync();
        try
        {
            Forget(session);
        }
        finally
        {
            _gate.Release();
        }

        await session.DisposeAsync();
    }

    /// <summary>The live browsers, newest first; only one container's when <paramref name="container"/> is given.</summary>
    public IReadOnlyList<BrowseSessionInfo> List(string? container = null)
    {
        lock (_byId)
        {
            return _byId.Values
                .Where(s => s.IsAlive && (string.IsNullOrWhiteSpace(container) || BrowseTarget.SameContainer(s.ContainerId, container)))
                .OrderByDescending(s => s.Created)
                .Select(s => new BrowseSessionInfo(s.Id, s.ContainerId, s.HostPort, s.Url, s.Client, s.OwnerViewerId, s.ViewerCount, s.Created))
                .ToList();
        }
    }

    /// <summary>Closes dead browsers and those nobody has watched for the idle limit; the ids closed.</summary>
    public async Task<IReadOnlyList<string>> ReapAsync()
    {
        var limit = TimeSpan.FromMinutes(options.CurrentValue.IdleMinutes);
        var now = clock.GetUtcNow();
        List<HostBrowserSession> candidates;
        lock (_byId)
        {
            candidates = _byId.Values.ToList();
        }

        var closed = new List<string>();
        foreach (var session in candidates)
        {
            var dead = !session.IsAlive;
            var idle = session.IdleFor(now);
            if (!dead && !(limit > TimeSpan.Zero && idle >= limit))
            {
                continue;
            }

            logger.LogInformation("Browse session {Verb} id={Id} ({Reason})", dead ? "dropped" : "reaped", session.Id, dead ? "browser gone" : $"idle {idle.TotalSeconds:0}s");
            await DestroyAsync(session);
            closed.Add(session.Id);
        }

        return closed;
    }

    /// <summary>The agent is stopping: no browser outlives it.</summary>
    public async ValueTask DisposeAsync()
    {
        List<HostBrowserSession> open;
        lock (_byId)
        {
            open = _byId.Values.ToList();
            _byId.Clear();
            _byKey.Clear();
        }

        foreach (var session in open)
        {
            await session.DisposeAsync();
        }
    }

    private HostBrowserSession? Find(Dictionary<string, HostBrowserSession> index, string key)
    {
        lock (_byId)
        {
            return index.GetValueOrDefault(key);
        }
    }

    private void Forget(HostBrowserSession session)
    {
        lock (_byId)
        {
            if (_byKey.GetValueOrDefault(session.RegistryKey) == session)
            {
                _byKey.Remove(session.RegistryKey);
            }

            if (_byId.GetValueOrDefault(session.Id) == session)
            {
                _byId.Remove(session.Id);
            }
        }
    }
}

/// <summary>Runs the idle reaper on a 30-second tick.</summary>
public sealed class HostBrowserReaper(HostBrowserSessions sessions) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await sessions.ReapAsync();
        }
    }
}
