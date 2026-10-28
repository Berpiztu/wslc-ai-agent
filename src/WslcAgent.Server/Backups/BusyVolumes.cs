namespace WslcAgent.Server.Backups;

/// <summary>
/// The volumes being backed up or restored now, by a volume's backup or a
/// container's: a container that uses one is not started meanwhile (its
/// Start, Restart, Run, its restart policy), since what it wrote would be
/// half in the backup, or overwritten by the restore. A volume may be held
/// twice, by a backup that stopped its containers and by the packing inside
/// it: it is free once every hold has gone.
/// </summary>
public sealed class BusyVolumes
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Doing, int Holds)> _busy = new(StringComparer.Ordinal);

    /// <summary><paramref name="volume"/> held while the returned hold lives: <paramref name="doing"/> says what (<c>backed up</c>, <c>restored</c>).</summary>
    public IDisposable Hold(string volume, string doing)
    {
        lock (_gate)
        {
            _busy[volume] = (doing, _busy.TryGetValue(volume, out var held) ? held.Holds + 1 : 1);
        }

        return new Release(() => Free(volume));
    }

    /// <summary>Some volume is held now: worth reading a container's mounts for.</summary>
    public bool Any
    {
        get
        {
            lock (_gate)
            {
                return _busy.Count > 0;
            }
        }
    }

    /// <summary>Why a container that uses these volumes cannot start now; null when none of them is held.</summary>
    public string? Why(string container, IEnumerable<string> volumes)
    {
        lock (_gate)
        {
            return volumes.FirstOrDefault(_busy.ContainsKey) is { } volume
                ? $"{container} cannot start while volume {volume} is being {_busy[volume].Doing}: wait for it, or cancel it."
                : null;
        }
    }

    private void Free(string volume)
    {
        lock (_gate)
        {
            if (_busy.TryGetValue(volume, out var held))
            {
                if (held.Holds > 1)
                {
                    _busy[volume] = held with { Holds = held.Holds - 1 };
                }
                else
                {
                    _busy.Remove(volume);
                }
            }
        }
    }

    /// <summary>A hold let go once, however often it is disposed.</summary>
    private sealed class Release(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                release();
            }
        }
    }
}
