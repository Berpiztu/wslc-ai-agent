using System.Text.RegularExpressions;

namespace WslcAgent.Server.Projects;

/// <summary>
/// The labels a container of a project carries, which is how the project is
/// found again: by <c>wslc</c> itself, with no file of the agent's. The config
/// is the service as it was read when the container was made, so a later save
/// tells the services that changed from those that did not.
/// </summary>
public static partial class ProjectLabels
{
    public const string Project = "ai.berpiztu.wslc.project";
    public const string Service = "ai.berpiztu.wslc.service";
    public const string Config = "ai.berpiztu.wslc.config";

    /// <summary><c>project/service</c>, as a launch request carries it, in its two parts; both empty for a container on its own.</summary>
    public static (string Project, string Service) Split(string value)
    {
        var cut = value.IndexOf('/');
        return cut <= 0 ? ("", "") : (value[..cut], value[(cut + 1)..]);
    }

    /// <summary>
    /// What a container's labels say of its project, from the list's
    /// <c>Labels</c>: one text, <c>key=value</c> joined by commas. A value of
    /// ours has no comma in it. All empty for a container on its own.
    /// </summary>
    public static (string Project, string Service, string Config) Of(string labels) =>
        (Value(ProjectLabel(), labels), Value(ServiceLabel(), labels), Value(ConfigLabel(), labels));

    private static string Value(Regex label, string labels) => label.Match(labels).Groups["value"].Value;

    [GeneratedRegex(@"(?:^|,)ai\.berpiztu\.wslc\.project=(?<value>[^,]*)")]
    private static partial Regex ProjectLabel();

    [GeneratedRegex(@"(?:^|,)ai\.berpiztu\.wslc\.service=(?<value>[^,]*)")]
    private static partial Regex ServiceLabel();

    [GeneratedRegex(@"(?:^|,)ai\.berpiztu\.wslc\.config=(?<value>[^,]*)")]
    private static partial Regex ConfigLabel();
}
