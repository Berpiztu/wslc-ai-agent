using MudBlazor;
using WslcAgent.ApiClient;

namespace WslcAgent.UI.Files;

/// <summary>The one way a screen writes a text file on the user's machine through <see cref="IClientFiles"/>.</summary>
public static class ClientFileSave
{
    public const string JsonType = "application/json";

    /// <summary>
    /// Reads the text (from the agent or from the form), saves it through the
    /// host's Save As and reports the destination; a cancelled picker says
    /// nothing, a failure shows its message.
    /// </summary>
    public static async Task TextAsync(IClientFiles files, ISnackbar snackbar, string suggestedName, Func<Task<string>> text, string mimeType = JsonType)
    {
        try
        {
            if (await files.SaveTextAsync(suggestedName, mimeType, await text()) is { } saved)
            {
                snackbar.Add($"Saved to {saved}", Severity.Success);
            }
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException or IOException or InvalidOperationException)
        {
            snackbar.Add(ex.Message, Severity.Error);
        }
    }
}
