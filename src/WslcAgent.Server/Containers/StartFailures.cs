using System.Collections.Concurrent;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Containers;

/// <summary>
/// Why a container did not start, the last time it was asked to. A container
/// that fails at its start is left there, made and stopped, and looks like
/// any other stopped one: what failed was said once, to whoever asked, on a
/// line that has passed. Kept here, the list says it on the container itself
/// for every client, and its logs, which have nothing of their own to show
/// for a start that never happened, say why. Forgotten when it starts, and
/// with the agent: it is about now, not history.
/// </summary>
public sealed class StartFailures
{
    /// <summary>By what the container was started with, its name or its id, and by the other one too once a list has shown which they are.</summary>
    private readonly ConcurrentDictionary<string, StartFailure> _failures = new(StringComparer.Ordinal);

    public void Failed(string container, string why) =>
        _failures[container] = new StartFailure(DateTimeOffset.Now, why.ReplaceLineEndings(" ").Trim());

    public void Started(string container) => _failures.TryRemove(container, out _);

    /// <summary>The failure kept for a container, by its name or its id.</summary>
    public StartFailure? Of(string container) => _failures.GetValueOrDefault(container);

    /// <summary>
    /// The list's rows with why each one did not start. A row that runs has
    /// started since, whoever started it and by whichever of its names: what
    /// was kept for it is let go. One that does not is said under both its
    /// id and its name from here on, so whoever asks by either finds it.
    /// </summary>
    public IReadOnlyList<ContainerSummary> Annotate(IReadOnlyList<ContainerSummary> containers) =>
        _failures.IsEmpty ? containers : [.. containers.Select(Annotated)];

    private ContainerSummary Annotated(ContainerSummary container)
    {
        if ((Of(container.Id) ?? Of(container.Name)) is not { } failure)
        {
            return container;
        }

        if (container.IsRunning)
        {
            Started(container.Id);
            Started(container.Name);
            return container;
        }

        _failures[container.Id] = _failures[container.Name] = failure;
        return container with { StartError = failure.Why };
    }
}

/// <summary>A start that failed: when, and what <c>wslc</c> said.</summary>
public sealed record StartFailure(DateTimeOffset At, string Why);
