using WslcAgent.ApiClient;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// Which shell a terminal opens inside a container, for the in-app terminal
/// and for a window on the agent's desktop alike.
/// </summary>
public static class ContainerShell
{
    /// <summary>
    /// bash, because its prompt shows the current directory and its readline
    /// gives Tab completion; <c>/bin/sh</c> is often dash, which shows a bare
    /// <c>#</c> and completes nothing.
    /// </summary>
    public const string Preferred = "/bin/bash -i";

    /// <summary>What busybox images (alpine and friends) have instead.</summary>
    public const string Fallback = "/bin/sh -i";

    /// <summary>
    /// The command as argv: what the user typed, or the best shell the image
    /// has when they typed nothing. Only steps down to <c>/bin/sh</c> when it
    /// is positively there — if both probes fail (a stopped container) bash
    /// stays, so the terminal reports the real error instead of a second
    /// misleading one.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveAsync(IWslcRunner wslc, string container, string? command, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(command))
        {
            return ShellWords.Split(command);
        }

        if (await HasAsync(wslc, container, "/bin/bash", cancellationToken) || !await HasAsync(wslc, container, "/bin/sh", cancellationToken))
        {
            return ShellWords.Split(Preferred);
        }

        return ShellWords.Split(Fallback);
    }

    private static async Task<bool> HasAsync(IWslcRunner wslc, string container, string path, CancellationToken cancellationToken)
    {
        try
        {
            await wslc.RunAsync(["exec", container, "test", "-x", path], TimeSpan.FromSeconds(15), cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is WslcException or TimeoutException)
        {
            return false;
        }
    }
}
