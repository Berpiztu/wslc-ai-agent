namespace WslcAgent.UI.Files;

/// <summary>
/// The files of the machine the user is sitting at, both ways. The browser
/// does all of this itself — a download link, its own file input — so it
/// reports <see cref="Supported"/> false and the UI keeps its own; a native
/// host has no download UI in its WebView and goes through the OS pickers
/// instead, so a downloaded file never lands on the agent and an uploaded one
/// is read from the disk it is already on.
/// </summary>
public interface IClientFiles
{
    /// <summary>False in the browser: the files of that machine belong to the browser.</summary>
    bool Supported { get; }

    /// <summary>
    /// Picks a destination and writes <paramref name="text"/> there as UTF-8.
    /// Returns what to show the user (a path, or the document name on Android),
    /// or null when the picker was cancelled.
    /// </summary>
    Task<string?> SaveTextAsync(string suggestedName, string mimeType, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks a destination and hands <paramref name="write"/> the stream to
    /// fill: a file of any size and any kind, written as it arrives instead of
    /// being held whole in memory. Returns what to show the user, or null when
    /// the picker was cancelled.
    /// </summary>
    Task<string?> SaveStreamAsync(string suggestedName, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks one folder for several files at once — a selection downloaded
    /// together is not worth one Save As each — or null when the picker was
    /// cancelled.
    /// </summary>
    Task<IClientFolder?> PickFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Picks files to send into a container; empty when the picker was closed
    /// with nothing chosen. Several at once, as the browser's own picker
    /// allows, and each becomes a transfer of its own.
    /// </summary>
    Task<IReadOnlyList<IClientFile>> PickFilesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A file the user picked, opened when its turn in the queue comes and not
/// before. It is a file of the OS, not of the WebView: an
/// <c>IBrowserFile</c> belongs to the input element it was chosen in, and the
/// moment that element goes — the view closes, the page changes — reading it
/// fails with <c>_blazorFilesById</c> of null, which is the one thing an
/// upload the user has walked away from must not do.
/// </summary>
public interface IClientFile
{
    string Name { get; }

    /// <summary>How big it is, for the ring on the container's row; zero when the OS does not say.</summary>
    long Size { get; }

    /// <summary>Its bytes, read as they are sent: a file of gigabytes is never held whole.</summary>
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>A folder the user picked, to write files into one after another.</summary>
public interface IClientFolder
{
    /// <summary>What to show the user: its path, or its name where there is no path (Android).</summary>
    string Display { get; }

    /// <summary>Creates <paramref name="name"/> in the folder and hands <paramref name="write"/> the stream to fill.</summary>
    Task WriteAsync(string name, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default);
}
