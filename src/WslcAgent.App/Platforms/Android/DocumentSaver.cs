using Android.App;
using Android.Content;
using Android.Provider;
using WslcAgent.UI.Files;

namespace WslcAgent.App;

/// <summary>
/// Save As on the device: the Storage Access Framework's create-document
/// picker (the user chooses Downloads, Drive, a folder…), then the bytes are
/// written through the content URI it returns. No storage permission is
/// needed. A selection downloaded together picks a folder once instead, with
/// the tree picker, and each file is created inside it.
/// <see cref="MainActivity.OnActivityResult"/> forwards both answers here.
/// </summary>
internal static class DocumentSaver
{
    private const int SaveRequestCode = 0x5A5C;  // "WSLC" save
    private const int FolderRequestCode = 0x5A5D;
    private static TaskCompletionSource<Android.Net.Uri?>? _pendingSave;
    private static TaskCompletionSource<Android.Net.Uri?>? _pendingFolder;

    /// <summary>The chosen document's display name, or null when the picker was cancelled.</summary>
    public static async Task<string?> SaveStreamAsync(string suggestedName, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        var activity = CurrentActivity;
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);

        var uri = await AskAsync(activity, intent, SaveRequestCode, ref _pendingSave);
        if (uri is null)
        {
            return null;
        }

        var resolver = Resolver(activity);
        await WriteAsync(resolver, uri, write, cancellationToken);
        return DisplayName(resolver, uri) ?? suggestedName;
    }

    /// <summary>The folder the user picked, to write several files into; null when the picker was cancelled.</summary>
    public static async Task<IClientFolder?> PickFolderAsync()
    {
        var activity = CurrentActivity;
        var tree = await AskAsync(activity, new Intent(Intent.ActionOpenDocumentTree), FolderRequestCode, ref _pendingFolder);
        if (tree is null)
        {
            return null;
        }

        var resolver = Resolver(activity);
        var folder = DocumentsContract.BuildDocumentUriUsingTree(tree, DocumentsContract.GetTreeDocumentId(tree))
            ?? throw new IOException("The chosen folder cannot be written to.");
        return new TreeFolder(resolver, folder, DisplayName(resolver, folder) ?? tree.LastPathSegment ?? "the chosen folder");
    }

    /// <summary>True when this was one of our pickers' answers; <see cref="MainActivity"/> then stops there.</summary>
    public static bool HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        var chosen = resultCode == Result.Ok ? data?.Data : null;
        if (requestCode == SaveRequestCode)
        {
            Interlocked.Exchange(ref _pendingSave, null)?.TrySetResult(chosen);
            return true;
        }

        if (requestCode == FolderRequestCode)
        {
            Interlocked.Exchange(ref _pendingFolder, null)?.TrySetResult(chosen);
            return true;
        }

        return false;
    }

    private static Activity CurrentActivity =>
        Platform.CurrentActivity ?? throw new InvalidOperationException("No current Android activity.");

    private static ContentResolver Resolver(Activity activity) =>
        activity.ContentResolver ?? throw new InvalidOperationException("No content resolver.");

    /// <summary>Opens one of the pickers and waits for the activity's answer; a picker left open by a previous call answers as cancelled.</summary>
    private static Task<Android.Net.Uri?> AskAsync(Activity activity, Intent intent, int requestCode, ref TaskCompletionSource<Android.Net.Uri?>? pending)
    {
        var waiting = new TaskCompletionSource<Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref pending, waiting)?.TrySetResult(null);
        activity.StartActivityForResult(intent, requestCode);
        return waiting.Task;
    }

    /// <summary>"wt": the picker may hand back an existing document, which is then truncated rather than appended to.</summary>
    private static async Task WriteAsync(ContentResolver resolver, Android.Net.Uri uri, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        await using var stream = resolver.OpenOutputStream(uri, "wt")
            ?? throw new IOException("Could not open the chosen document for writing.");
        await write(stream, cancellationToken);
    }

    /// <summary>What to show the user: the document's name, or the URI's tail when the provider refuses metadata.</summary>
    private static string? DisplayName(ContentResolver resolver, Android.Net.Uri uri)
    {
        try
        {
            using var cursor = resolver.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst() && cursor.GetString(0) is { Length: > 0 } name)
            {
                return name;
            }
        }
        catch (Java.Lang.Exception)
        {
        }

        return uri.LastPathSegment;
    }

    /// <summary>A folder of the Storage Access Framework: each file is created in it as its own document.</summary>
    private sealed class TreeFolder(ContentResolver resolver, Android.Net.Uri folder, string display) : IClientFolder
    {
        public string Display => display;

        public async Task WriteAsync(string name, string mimeType, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
        {
            var document = DocumentsContract.CreateDocument(resolver, folder, mimeType, name)
                ?? throw new IOException($"{name} could not be created in {display}.");
            await DocumentSaver.WriteAsync(resolver, document, write, cancellationToken);
        }
    }
}
