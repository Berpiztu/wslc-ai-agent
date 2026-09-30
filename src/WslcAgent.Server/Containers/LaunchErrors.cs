using System.Text.RegularExpressions;
using WslcAgent.ApiClient;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Containers;

/// <summary>
/// Which field of the launch form a failed <c>wslc container run|create</c>
/// is about, read from what the CLI said. The texts are the CLI's own,
/// captured on a real machine (a name in use, a port that would not map, a
/// network that is not there, a host path that does not exist, a memory size
/// it could not read, an image it could not pull, a user it could not find,
/// an executable it could not exec). What matches no field stays general.
/// </summary>
public static partial class LaunchErrors
{
    private static readonly (Regex Pattern, string Field)[] Fields =
    [
        (NameInUse(), LaunchFields.Name),
        (PortRefused(), LaunchFields.Publish),
        (NetworkRefused(), LaunchFields.Networks),
        (VolumeRefused(), LaunchFields.Volumes),
        (MemoryRefused(), LaunchFields.Memory),
        (CpusRefused(), LaunchFields.Cpus),
        (ImageRefused(), LaunchFields.Image),
        (UserRefused(), LaunchFields.User),
        (WorkdirRefused(), LaunchFields.Workdir),
        (StopTimeoutRefused(), LaunchFields.StopTimeout),
        (HealthRefused(), LaunchFields.HealthCmd),
        (PublicNameRefused(), LaunchFields.PublicNames),
    ];

    /// <summary>The same exception with its fields read, unless it already carries some.</summary>
    public static WslcException WithFields(WslcException failure, ContainerLaunchRequest request) =>
        failure.Fields.Count > 0 ? failure : new WslcException(failure.Message, failure.Result) { Fields = FieldsOf(failure.Message, request) };

    /// <summary>
    /// The fields with a note that is about none of them (what a recreate did with
    /// the container), shown above the fields. Without fields the note is already
    /// in the message, which is shown whole.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WithNote(IReadOnlyDictionary<string, string> fields, string note)
    {
        if (fields.Count == 0)
        {
            return fields;
        }

        var noted = new Dictionary<string, string>(fields, StringComparer.Ordinal) { [LaunchFields.General] = note };
        return noted;
    }

    /// <summary>Field → the CLI's reason, for every field the message is about; empty when it is about none.</summary>
    public static IReadOnlyDictionary<string, string> FieldsOf(string message, ContainerLaunchRequest? request)
    {
        var reason = Reason(message);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (pattern, field) in Fields)
        {
            if (pattern.IsMatch(message))
            {
                fields[field] = reason;
            }
        }

        if (ExecutableMissing().Match(message) is { Success: true } exec)
        {
            fields[ExecutableField(exec.Groups["name"].Value, request)] = reason;
        }

        return fields;
    }

    /// <summary>
    /// The executable runc could not find belongs to the entrypoint when it is
    /// the entrypoint's first word, to the command when it is the command's; an
    /// entrypoint that is set is the one that runs, so it takes what is neither.
    /// </summary>
    private static string ExecutableField(string executable, ContainerLaunchRequest? request)
    {
        if (request is null)
        {
            return LaunchFields.Command;
        }

        var entrypoint = ShellWords.Split(request.Entrypoint).FirstOrDefault() ?? "";
        var command = ShellWords.Split(request.Command).FirstOrDefault() ?? "";
        if (command.Length > 0 && command == executable && entrypoint != executable)
        {
            return LaunchFields.Command;
        }

        return entrypoint.Length > 0 ? LaunchFields.Entrypoint : LaunchFields.Command;
    }

    /// <summary>
    /// What the CLI said, without the command line in front of it and without its
    /// "Error code" and "file an issue" lines: the sentence a field can show.
    /// </summary>
    public static string Reason(string message)
    {
        var text = CommandPrefix().Replace(message, "", 1);
        var cut = text.IndexOf("Error code:", StringComparison.Ordinal);
        if (cut > 0)
        {
            text = text[..cut];
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("If this error was unexpected", StringComparison.Ordinal) && !line.StartsWith("Usage:", StringComparison.Ordinal));
        return string.Join(' ', lines).Trim();
    }

    [GeneratedRegex(@"^wslc [^\n]*?: (?=\S)")]
    private static partial Regex CommandPrefix();

    [GeneratedRegex(@"already in use by container|ERROR_ALREADY_EXISTS|name is already in use", RegexOptions.IgnoreCase)]
    private static partial Regex NameInUse();

    [GeneratedRegex(@"Failed to map port|WSAEADDRINUSE|Invalid port specified|port is already allocated|address already in use|invalid publish", RegexOptions.IgnoreCase)]
    private static partial Regex PortRefused();

    [GeneratedRegex(@"Network not found|WSLC_E_NETWORK_NOT_FOUND|IP address requires a user-defined network|invalid IP|network-alias|no such network", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkRefused();

    [GeneratedRegex(@"Failed to create volume|ERROR_PATH_NOT_FOUND|invalid mount|bind source path|invalid volume", RegexOptions.IgnoreCase)]
    private static partial Regex VolumeRefused();

    [GeneratedRegex(@"Invalid memory option|memory size", RegexOptions.IgnoreCase)]
    private static partial Regex MemoryRefused();

    [GeneratedRegex(@"Invalid cpus|cpus option|invalid value for cpus", RegexOptions.IgnoreCase)]
    private static partial Regex CpusRefused();

    [GeneratedRegex(@"WSLC_E_IMAGE_NOT_FOUND|pull access denied|repository does not exist|manifest unknown|Image '[^']*' not found|no such image", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRefused();

    [GeneratedRegex(@"unable to find user|unable to find group|passwd file", RegexOptions.IgnoreCase)]
    private static partial Regex UserRefused();

    [GeneratedRegex(@"working directory|workdir", RegexOptions.IgnoreCase)]
    private static partial Regex WorkdirRefused();

    [GeneratedRegex(@"stop.timeout", RegexOptions.IgnoreCase)]
    private static partial Regex StopTimeoutRefused();

    [GeneratedRegex(@"health.?(cmd|check|interval|timeout|retries|start.period)", RegexOptions.IgnoreCase)]
    private static partial Regex HealthRefused();

    [GeneratedRegex(@"Reverse proxy|public name", RegexOptions.IgnoreCase)]
    private static partial Regex PublicNameRefused();

    /// <summary>runc: <c>exec: "tini -s --": executable file not found in $PATH</c>, or a missing file the exec named.</summary>
    [GeneratedRegex(@"exec: ""(?<name>[^""]*)"": (executable file not found|no such file or directory|permission denied)", RegexOptions.IgnoreCase)]
    private static partial Regex ExecutableMissing();
}
