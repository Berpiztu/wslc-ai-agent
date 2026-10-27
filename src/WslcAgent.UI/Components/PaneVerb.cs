using WslcAgent.ApiClient;

namespace WslcAgent.UI.Components;

/// <summary>
/// The verb on the title line of a project's window of two panes (Save, Patch,
/// Update): busy from the click to its end, its veil drawn over the pane of the
/// container it runs on before anything else is done (a form's check
/// included), the menu left free for another, and what the agent
/// refuses kept to be said over the pane. <paramref name="repaint"/> tells the
/// window's body and its title line, which MudBlazor draws apart.
/// </summary>
public sealed class PaneVerb(Action repaint)
{
    public bool Busy { get; private set; }

    /// <summary>The container the verb runs on, by id, while it runs: only its pane is veiled, and another can be chosen meanwhile.</summary>
    public string? For { get; private set; }

    /// <summary>Why the last run did not go through; null when it did, or nothing has run.</summary>
    public string? Error { get; private set; }

    /// <summary>Forgets the last refusal: another container or step was chosen.</summary>
    public void Clear() => Error = null;

    /// <summary>Runs the verb on the container <paramref name="container"/> (its id); true when it went through.</summary>
    public async Task<bool> RunAsync(string container, Func<Task<bool>> verb)
    {
        Busy = true;
        For = container;
        Error = null;
        repaint();

        // The click's handler draws only when it first waits: it waits here, so the veil is on screen now.
        await Task.Yield();
        try
        {
            return await verb();
        }
        catch (Exception ex) when (ex is AgentApiException or HttpRequestException)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            Busy = false;
            For = null;
            repaint();
        }
    }
}
