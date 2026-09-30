namespace WslcAgent.Server.Browse;

/// <summary>
/// What the host browsers may cost, bound from the <c>Browse</c> configuration
/// section: each one is a real Edge process on the host. Zero disables a limit.
/// </summary>
public sealed class BrowseOptions
{
    public const string Section = "Browse";

    /// <summary>Minutes a browser may sit with no pane attached before it is closed.</summary>
    public int IdleMinutes { get; set; } = 30;

    /// <summary>Live browsers across every client; a new one past this is refused.</summary>
    public int MaxSessions { get; set; } = 8;
}
