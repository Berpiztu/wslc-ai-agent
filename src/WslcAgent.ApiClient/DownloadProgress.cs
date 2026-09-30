namespace WslcAgent.ApiClient;

/// <summary>
/// How far a download has got. <paramref name="Total"/> is null when the agent
/// does not say how big the file is, and the caller then shows what has
/// arrived instead of a percentage.
/// </summary>
public readonly record struct DownloadProgress(long Received, long? Total)
{
    /// <summary>0 to 100, or null when the whole size is unknown.</summary>
    public int? Percent => Total is > 0 ? (int)Math.Clamp(Received * 100 / Total.Value, 0, 100) : null;

    /// <summary>"37%" while the size is known, "12.4 MiB" while it is not.</summary>
    public string Text => Percent is { } percent ? $"{percent}%" : Bytes.Humanize(Received);
}
