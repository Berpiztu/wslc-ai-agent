using System.Reflection;

namespace WslcAgent.Server.Overview;

/// <summary>
/// A file the agent ships inside itself and a development build writes back
/// to the repository it is built from, so the next installer ships what was
/// designed on the development agent: the default dashboard, the cards'
/// default layouts. The file is embedded under <paramref name="resource"/>;
/// a Debug build carries its source path as the assembly metadata
/// <paramref name="sourceKey"/> (the project file), a Release build — what the
/// installer publishes — carries none and writes nothing.
/// </summary>
public sealed class ShippedFile(string resource, string sourceKey)
{
    private readonly string? _source = typeof(ShippedFile).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == sourceKey)?.Value;

    private readonly Lock _gate = new();

    /// <summary>Whether this agent writes the file back to where it is built from (a development build).</summary>
    public bool Writable => _source is not null;

    /// <summary>
    /// The file: the repository's on a development build, so what was written
    /// back a moment ago is what is read; the one embedded in the agent
    /// otherwise. Null when there is none.
    /// </summary>
    public string? Read()
    {
        lock (_gate)
        {
            if (_source is not null && ReadFile(_source) is { } written)
            {
                return written;
            }
        }

        using var stream = typeof(ShippedFile).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd() is { Length: > 0 } text ? text : null;
    }

    /// <summary>Written back to the repository; false when this build does not write it (a release build) or there is nothing to write.</summary>
    public bool Write(string text)
    {
        if (_source is null || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        lock (_gate)
        {
            WriteFile(_source, text);
            return true;
        }
    }

    /// <summary>Written where it goes, its folder made first.</summary>
    public static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A file written by hand, or half-written, must not stop the agent: there is simply none.</summary>
    public static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path) is { Length: > 0 } text ? text : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
