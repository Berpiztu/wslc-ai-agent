using System.Text.Json;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Containers;

/// <summary>
/// What a container was launched with and <c>wslc container inspect</c> does
/// not say back: its host and domain names, its DNS, its tmpfs mounts, the
/// size of <c>/dev/shm</c> and its stop signal. Seen in wslc 3.0.2: the flags
/// are taken, and inspect shows the labels and the ulimits alone. The agent
/// writes them on the container itself, in one label, so View &amp; edit shows
/// them and a recreate keeps them, whichever agent reads the container next:
/// as the GPUs are read from the runtime's own metadata label.
/// </summary>
public sealed record LaunchMemo(
    string Hostname,
    string Domainname,
    IReadOnlyList<string> Dns,
    IReadOnlyList<string> DnsSearch,
    IReadOnlyList<string> DnsOptions,
    IReadOnlyList<string> Tmpfs,
    string ShmSize,
    string StopSignal)
{
    /// <summary>The label the memo travels in.</summary>
    public const string Label = "ai.berpiztu.wslc.launch";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A label that is not the user's: the agent's own, or the runtime's. Neither is shown in the form, nor sent back as one of theirs.</summary>
    public static bool IsOwn(string key) =>
        key.StartsWith("ai.berpiztu.wslc.", StringComparison.Ordinal) || key.StartsWith("com.microsoft.wsl.", StringComparison.Ordinal);

    /// <summary>The label to launch with, <c>key=value</c>; empty when the request sets none of what the memo keeps.</summary>
    public static string Of(ContainerLaunchRequest request)
    {
        var nothing = request.Hostname.Length == 0 && request.Domainname.Length == 0 && request.ShmSize.Length == 0 && request.StopSignal.Length == 0
            && request.Dns.Count == 0 && request.DnsSearch.Count == 0 && request.DnsOptions.Count == 0 && request.Tmpfs.Count == 0;
        if (nothing)
        {
            return "";
        }

        var memo = new LaunchMemo(request.Hostname, request.Domainname, request.Dns, request.DnsSearch, request.DnsOptions, request.Tmpfs, request.ShmSize, request.StopSignal);
        return $"{Label}={JsonSerializer.Serialize(memo, Json)}";
    }

    /// <summary>The memo a container's label holds; null when it has none, or one that cannot be read.</summary>
    public static LaunchMemo? Read(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<LaunchMemo>(value, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
