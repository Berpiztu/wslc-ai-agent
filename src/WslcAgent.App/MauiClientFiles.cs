using WslcAgent.UI.Files;

namespace WslcAgent.App;

/// <summary>
/// The files of the machine the client runs on, both ways, through the OS
/// pickers (Windows <c>FileSavePicker</c> and <c>FileOpenPicker</c>, Android's
/// Storage Access Framework): the WebView has no download UI, so a downloaded
/// file lands where the user is and never on the agent host, and an uploaded
/// one is read from its own path instead of from the WebView's input element,
/// which does not outlive the screen it was on. A file of any size goes
/// through <see cref="SaveStreamAsync"/> or <see cref="IClientFile"/>, written
/// and read as it travels; a selection downloaded together picks one folder
/// instead of asking once per file.
/// </summary>
internal sealed class MauiClientFiles : IClientFiles
{
    public bool Supported => DeviceInfo.Platform == DevicePlatform.WinUI || DeviceInfo.Platform == DevicePlatform.Android;

    public Task<string?> SaveTextAsync(string suggestedName, string mimeType, string text, CancellationToken cancellationToken = default) =>
        SaveStreamAsync(suggestedName, mimeType, async (stream, token) =>
        {
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            await writer.WriteAsync(text.AsMemory(), token);
        }, cancellationToken);

    public async Task<string?> SaveStreamAsync(string suggestedName, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        if (await PickSaveFileAsync(suggestedName) is not { } path)
        {
            return null;
        }

        await using (var file = File.Create(path))
        {
            await write(file, cancellationToken);
        }

        return path;
#elif ANDROID
        return await DocumentSaver.SaveStreamAsync(suggestedName, mimeType, write, cancellationToken);
#else
        throw new InvalidOperationException("This client cannot save files.");
#endif
    }

    public async Task<IClientFolder?> PickFolderAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
        };
        // Without a filter the picker throws as it opens; "*" is every folder.
        picker.FileTypeFilter.Add("*");
        WithWindow(picker);

        var folder = await picker.PickSingleFolderAsync();
        return folder is null ? null : new WindowsFolder(folder.Path);
#elif ANDROID
        return await DocumentSaver.PickFolderAsync();
#else
        throw new InvalidOperationException("This client cannot save files.");
#endif
    }

    public async Task<IReadOnlyList<IClientFile>> PickFilesAsync(CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
        };
        // Without a filter the picker throws as it opens; "*" is every file.
        picker.FileTypeFilter.Add("*");
        WithWindow(picker);

        var chosen = await picker.PickMultipleFilesAsync();
        return chosen is null ? [] : [.. chosen.Select(file => new WindowsFile(file.Path))];
#elif ANDROID
        // Cancelling answers null, which is nothing chosen and not a failure;
        // the list is declared as holding nulls too, and a null is no file.
        var chosen = await FilePicker.Default.PickMultipleAsync();
        return chosen is null ? [] : [.. await Task.WhenAll(chosen.OfType<FileResult>().Select(AndroidFile.OfAsync))];
#else
        throw new InvalidOperationException("This client cannot pick files.");
#endif
    }

#if WINDOWS
    /// <summary>The Save As dialog; null when the user closed it.</summary>
    private static async Task<string?> PickSaveFileAsync(string suggestedName)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
        };
        picker.FileTypeChoices.Add(TypeLabel(suggestedName), [Extension(suggestedName)]);
        picker.FileTypeChoices.Add("All files", ["."]);
        WithWindow(picker);

        return (await picker.PickSaveFileAsync())?.Path;
    }

    /// <summary>A WinUI picker belongs to a window: without the handle it never opens.</summary>
    private static void WithWindow(object picker)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is { } native)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(native));
        }
    }

    private static string Extension(string name) => Path.GetExtension(name) is { Length: > 0 } extension ? extension : ".txt";

    private static string TypeLabel(string name) => Extension(name).TrimStart('.').ToUpperInvariant();

    /// <summary>A file of the Windows filesystem, opened from its own path when its turn in the queue comes.</summary>
    private sealed class WindowsFile(string path) : IClientFile
    {
        public string Name => Path.GetFileName(path);

        public long Size => File.Exists(path) ? new FileInfo(path).Length : 0;

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(File.OpenRead(path));
    }

    /// <summary>A folder of the Windows filesystem: each file is created in it by name.</summary>
    private sealed class WindowsFolder(string path) : IClientFolder
    {
        public string Display => path;

        public async Task WriteAsync(string name, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
        {
            await using var file = File.Create(Path.Combine(path, Path.GetFileName(name)));
            await write(file, cancellationToken);
        }
    }
#endif

#if ANDROID
    /// <summary>
    /// A file of the Storage Access Framework, kept as the result the picker
    /// gave: it opens its content URI again for every read, so its turn in the
    /// queue may come long after the picker closed. Its size is asked once, as
    /// it is picked, because a content stream only answers that while it is
    /// open.
    /// </summary>
    private sealed class AndroidFile(FileResult file, long size) : IClientFile
    {
        public string Name => file.FileName;

        public long Size => size;

        public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) => await file.OpenReadAsync();

        public static async Task<IClientFile> OfAsync(FileResult file)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                return new AndroidFile(file, stream.CanSeek ? stream.Length : 0);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // A size is a courtesy: without it the ring counts what arrives.
                return new AndroidFile(file, 0);
            }
        }
    }
#endif
}
