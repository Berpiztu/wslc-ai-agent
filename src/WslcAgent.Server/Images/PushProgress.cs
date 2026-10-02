namespace WslcAgent.Server.Images;

/// <summary>
/// What a <c>wslc image push</c> terminal output says: the layers seen and how
/// far each got (uploading is all of it; one the registry already had, or
/// mounted from another repository, counts as done) and a status line to show.
/// The failure and the log are read as a pull's (<see cref="PullProgress"/>).
/// </summary>
public static class PushProgress
{
    /// <summary>The percentage over every layer seen and the status line.</summary>
    public static (int Pct, string Status) Read(string output)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var progress = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var status = "Pushing...";

        foreach (var cleaned in PullProgress.Lines(output))
        {
            var match = PullProgress.LayerLine().Match(cleaned);
            var layer = match.Success ? match.Groups[1].Value.ToLowerInvariant() : "";
            var message = match.Success ? match.Groups[2].Value.Trim() : cleaned;
            if (layer.Length > 0)
            {
                seen.Add(layer);
            }

            if (message.Contains("Pushed") || message.Contains("Layer already exists") || message.Contains("Mounted from"))
            {
                PullProgress.Set(progress, layer, 1.0);
                status = message;
            }
            else if (message.Contains("Pushing"))
            {
                if (layer.Length > 0 && PullProgress.Sizes(message) is { } size)
                {
                    PullProgress.Raise(progress, layer, Math.Min(size.Current / size.Total, 1.0));
                }

                status = "Uploading...";
            }
            else if (message.Contains("Preparing") || message.Contains("Waiting"))
            {
                status = "Preparing layers...";
            }
            else if (message.Contains("The push refers to repository"))
            {
                status = "Pushing to registry...";
            }
            else if (message.Contains("digest: sha256:"))
            {
                foreach (var existing in seen)
                {
                    progress[existing] = 1.0;
                }

                status = "Pushed";
            }
        }

        var pct = seen.Count == 0 ? 0 : Math.Clamp((int)(seen.Sum(l => progress.GetValueOrDefault(l)) / seen.Count * 100), 0, 100);
        if (seen.Count == 0 && output.Trim().Length > 0)
        {
            status = "Running (no incremental progress from wslc)";
        }

        return (pct, status);
    }
}
