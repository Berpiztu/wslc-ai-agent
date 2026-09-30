using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Mcp;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// The temporary containers that let the Files view browse what is not a
/// running container: an image (a container of it kept alive with
/// <c>sleep infinity</c>) and a volume (a small local image with the volume
/// mounted at <see cref="VolumeMount"/>). Both are hidden from the container
/// lists and removed when the view closes.
/// </summary>
public sealed class FilesHelpers(IWslcRunner wslc, IImageService images, ILogger<FilesHelpers> logger)
{
    public const string ImagePrefix = "wslc-agent-files-";
    public const string VolumePrefix = "wslc-agent-volfiles-";

    /// <summary>Where a volume appears inside its helper; the Files view treats it as <c>/</c>.</summary>
    public const string VolumeMount = "/mnt/wslc-volume";

    /// <summary>Local images a volume helper prefers: small ones with a shell and coreutils.</summary>
    private static readonly string[] PreferredHelperImages = ["alpine:latest", "alpine", "busybox:latest", "busybox", "agent0ai/agent-zero:latest", "agent0ai/agent-zero"];

    private static readonly TimeSpan MountPoll = TimeSpan.FromMilliseconds(250);
    private const int MountAttempts = 20;

    /// <summary>One helper per volume, reused while it answers.</summary>
    private readonly ConcurrentDictionary<string, string> _volumeHelpers = new(StringComparer.Ordinal);

    /// <summary>
    /// A container of <paramref name="reference"/> that only sleeps, so its
    /// entrypoint cannot run or exit. Never pulls: browsing a local image must
    /// not turn into a registry request when the image has just been removed.
    /// </summary>
    public async Task<FilesSession> OpenImageAsync(string reference, CancellationToken cancellationToken = default)
    {
        var image = WslcArgs.Require(reference, "image reference");
        var name = $"{ImagePrefix}{Hash(image, 8)}-{Guid.NewGuid():N}"[..(ImagePrefix.Length + 17)];
        try
        {
            var result = await wslc.RunAsync(
                ["container", "run", "--detach", "--name", name, "--entrypoint", "sleep", "--pull", "never", image, "infinity"],
                cancellationToken: cancellationToken);
            return new FilesSession(IdOrName(result.Stdout, name), "/");
        }
        catch (WslcException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("No such image", StringComparison.OrdinalIgnoreCase))
        {
            throw new WslcException($"Image {image} is no longer available. Refresh the images list.", ex.Result);
        }
    }

    /// <summary>The volume mounted in its helper, started (or reused) and waited for until the mount is visible.</summary>
    public async Task<FilesSession> OpenVolumeAsync(string volume, CancellationToken cancellationToken = default)
    {
        var name = WslcArgs.Require(volume, "volume name");
        if (_volumeHelpers.TryGetValue(name, out var existing))
        {
            if (await MountVisibleAsync(existing, cancellationToken))
            {
                return new FilesSession(existing, VolumeMount);
            }

            _volumeHelpers.TryRemove(name, out _);
            await RemoveQuietlyAsync(existing);
        }

        var helper = VolumePrefix + Hash(name, 10);
        await RemoveQuietlyAsync(helper);
        var image = await HelperImageAsync(cancellationToken);
        var result = await wslc.RunAsync(
            ["container", "run", "--detach", "--name", helper, "--entrypoint", "sleep", "--volume", $"{name}:{VolumeMount}", image, "infinity"],
            cancellationToken: cancellationToken);
        var container = IdOrName(result.Stdout, helper);
        if (!await MountVisibleAsync(container, cancellationToken))
        {
            await RemoveQuietlyAsync(container);
            throw new WslcException($"Guest volume mount not ready at {VolumeMount}", result);
        }

        _volumeHelpers[name] = container;
        return new FilesSession(container, VolumeMount);
    }

    /// <summary>Stops and removes an image helper, found by its name; anything that is not one of these helpers is refused.</summary>
    public async Task CloseImageAsync(string container, CancellationToken cancellationToken = default)
    {
        var result = await wslc.RunAsync(["container", "inspect", WslcArgs.Require(container, "container"), "--format", "json"], cancellationToken: cancellationToken);
        if (!ContainerService.IsHelper(ContainerInspection.Parse(result).Name))
        {
            throw new ArgumentException($"{container} is not a files helper container.", nameof(container));
        }

        await RemoveQuietlyAsync(container);
    }

    /// <summary>Stops and removes the volume's helper, by the one this agent started or by its stable name.</summary>
    public Task CloseVolumeAsync(string volume)
    {
        var name = WslcArgs.Require(volume, "volume name");
        return RemoveQuietlyAsync(_volumeHelpers.TryRemove(name, out var container) ? container : VolumePrefix + Hash(name, 10));
    }

    private static string IdOrName(string stdout, string name) =>
        ContainerService.ContainerIdFrom(stdout) is { Length: > 0 } id ? id : name;

    private async Task<bool> MountVisibleAsync(string container, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MountAttempts; attempt++)
        {
            try
            {
                await wslc.RunAsync(["exec", container, "test", "-d", VolumeMount], cancellationToken: cancellationToken);
                return true;
            }
            catch (WslcException)
            {
                await Task.Delay(MountPoll, cancellationToken);
            }
        }

        return false;
    }

    /// <summary>A local image to host the volume, preferring the small ones; the first tagged image otherwise.</summary>
    private async Task<string> HelperImageAsync(CancellationToken cancellationToken)
    {
        var references = (await images.ListAsync(cancellationToken)).Images
            .Where(i => !i.IsDangling)
            .Select(i => i.Reference)
            .ToList();
        foreach (var wanted in PreferredHelperImages)
        {
            var repository = wanted.Split(':')[0];
            if (references.FirstOrDefault(r => r.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                    || r.StartsWith(repository + ":", StringComparison.OrdinalIgnoreCase)
                    || r.Equals(repository, StringComparison.OrdinalIgnoreCase)) is { } found)
            {
                return found;
            }
        }

        return references.FirstOrDefault()
            ?? throw new InvalidOperationException("No local image available to browse guest volumes; pull alpine first.");
    }

    /// <summary>Best effort: the helper may already be gone.</summary>
    private async Task RemoveQuietlyAsync(string container)
    {
        try
        {
            await wslc.RunAsync(["container", "rm", "--force", container]);
        }
        catch (Exception ex) when (ex is WslcException or TimeoutException)
        {
            logger.LogDebug("files helper {Container} not removed: {Message}", container, ex.Message);
        }
    }

    private static string Hash(string text, int length) =>
        Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..length];
}
