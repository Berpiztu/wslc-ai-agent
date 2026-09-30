using System.Text.RegularExpressions;
using WslcAgent.ApiClient.Contracts;

namespace WslcAgent.Server.Publishing;

/// <summary>
/// One row of the launch form's Ports popup, <c>containerPort:name</c>
/// (<c>8080:open-webui</c>): the port inside the container and the bare name;
/// the suffix and the domain of Settings → Publishing are added by the agent
/// (<c>open-webui-home.example.com</c>). A name with dots is a whole
/// hostname, so a name under another domain survives a change of settings.
/// </summary>
public static partial class PublicNameRow
{
    /// <summary>The row's port and hostname; an <see cref="ArgumentException"/> says what is wrong with it.</summary>
    public static (int Port, string Hostname) Parse(string row, PublishingSettings settings)
    {
        var match = Row().Match(row.Trim().ToLowerInvariant());
        if (!match.Success)
        {
            throw new ArgumentException($"Public name '{row}': write it as containerPort:name, like 8080:open-webui.");
        }

        var port = int.Parse(match.Groups["port"].Value);
        if (port is < 1 or > 65535)
        {
            throw new ArgumentException($"Public name '{row}': the container port has to be between 1 and 65535.");
        }

        var name = match.Groups["prefix"].Value;
        return (port, name.Contains('.') ? name : $"{name}{settings.NameSuffix}.{settings.Domain}");
    }

    /// <summary>The row a publication reads back as: the bare name when the suffix and domain are Settings', the whole hostname otherwise.</summary>
    public static string Format(Publication publication, PublishingSettings settings)
    {
        var tail = $"{settings.NameSuffix}.{settings.Domain}";
        var name = publication.Hostname.EndsWith(tail, StringComparison.OrdinalIgnoreCase)
            ? publication.Hostname[..^tail.Length]
            : publication.Hostname;
        return $"{publication.ContainerPort}:{name}";
    }

    [GeneratedRegex(@"^(?<port>\d{1,5}):(?<prefix>[a-z0-9]([a-z0-9.-]*[a-z0-9])?)$")]
    private static partial Regex Row();
}
