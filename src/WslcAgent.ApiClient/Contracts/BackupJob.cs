namespace WslcAgent.ApiClient.Contracts;

/// <summary>
/// A container backup (<c>wslc container export</c> to a tar on the agent):
/// a job the UI polls, then downloads, then finishes or
/// discards; the archive stays on the agent until then so a failed download
/// can be retried.
/// </summary>
/// <param name="Id">Job id.</param>
/// <param name="Container">The container as requested.</param>
/// <param name="State"><see cref="BackupState"/>.</param>
/// <param name="Step">The step in progress or reached: <see cref="BackupStep"/>.</param>
/// <param name="Filename">Download name, <c>name-yyyy-MM-dd.tar</c>.</param>
/// <param name="Bytes">Size of the archive so far.</param>
/// <param name="Error">Why it failed, empty otherwise.</param>
public sealed record BackupJob(
    string Id,
    string Container,
    string State,
    string Step,
    string Filename,
    long Bytes,
    string Error);

public static class BackupState
{
    public const string Running = "running";
    public const string Ready = "ready";
    public const string Saving = "saving";
    public const string Error = "error";
    public const string Cancelled = "cancelled";
}

/// <summary>The fixed steps of a backup, in order, with their labels.</summary>
public static class BackupStep
{
    public const string Check = "check";
    public const string Archive = "archive";
    public const string Ready = "ready";

    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        (Check, "Checking container"),
        (Archive, "Creating archive"),
        (Ready, "Archive ready"),
    ];
}
