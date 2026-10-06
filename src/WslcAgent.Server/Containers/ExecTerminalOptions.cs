namespace WslcAgent.Server.Containers;

/// <summary>
/// What an exec terminal session may cost, bound from the <c>Terminal</c>
/// configuration section. The limits: a shell left open in a
/// forgotten tab must not hold a process for ever, and a client must not be
/// able to open them without end. Zero disables a limit.
/// </summary>
public sealed class ExecTerminalOptions
{
    public const string Section = "Terminal";

    /// <summary>Sessions open at once on the whole agent.</summary>
    public int MaxSessions { get; set; } = 20;

    /// <summary>Seconds without input or output before the session is closed.</summary>
    public int IdleTimeoutSeconds { get; set; } = 1800;

    /// <summary>Seconds a session may live however busy it is.</summary>
    public int MaxLifetimeSeconds { get; set; } = 28800;

    /// <summary>Largest client message accepted; a bigger one closes the session.</summary>
    public int MaxMessageBytes { get; set; } = 65536;

    /// <summary>Seconds a disconnected session is kept for resumption.</summary>
    public int ResumeTimeoutSeconds { get; set; } = 600;

    /// <summary>Maximum UTF-8 bytes of output retained while disconnected.</summary>
    public int MaxBufferedBytes { get; set; } = 262144;
}
