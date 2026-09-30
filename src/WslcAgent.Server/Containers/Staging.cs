namespace WslcAgent.Server.Containers;

/// <summary>
/// The agent's own scratch space for bytes on their way in or out of a
/// container: <c>wslc container cp</c> only moves files between the host and
/// the container, so every upload, download and edit lands here first and is
/// deleted straight after.
/// </summary>
public static class Staging
{
    /// <summary>
    /// A path of its own for this transfer, under the name the caller says the
    /// file travels with: Windows has names it does not accept and the copy
    /// has names it cannot carry, so what lands in the container is renamed
    /// there (<c>ContainerFiles</c>) rather than trusted to arrive whole.
    /// </summary>
    public static string Path(string what, string name)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wslc-files-{Safe(what)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return System.IO.Path.Combine(folder, Safe(name) is { Length: > 0 } file ? file : "file");
    }

    /// <summary>Removes the staged file and the folder it was alone in; failure here is never the user's problem.</summary>
    public static void Discard(string staged)
    {
        try
        {
            File.Delete(staged);
            Directory.Delete(System.IO.Path.GetDirectoryName(staged)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Another process still holds it: the temp folder is cleaned by the system.
        }
    }

    /// <summary>A file name Windows accepts, whatever the container called it.</summary>
    private static string Safe(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
