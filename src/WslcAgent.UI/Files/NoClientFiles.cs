namespace WslcAgent.UI.Files;

/// <summary>The browser host: it carries files both ways itself, so nothing here is used.</summary>
public sealed class NoClientFiles : IClientFiles
{
    public bool Supported => false;

    public Task<IReadOnlyList<IClientFile>> PickFilesAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The browser build picks files through the browser.");

    public Task<string?> SaveTextAsync(string suggestedName, string mimeType, string text, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The browser build saves downloads through the browser.");

    public Task<string?> SaveStreamAsync(string suggestedName, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The browser build saves downloads through the browser.");

    public Task<IClientFolder?> PickFolderAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The browser build saves downloads through the browser.");
}
