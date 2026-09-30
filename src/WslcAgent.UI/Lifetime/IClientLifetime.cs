namespace WslcAgent.UI.Lifetime;

/// <summary>
/// Closing the application on request: the way out of a native client whose
/// agent stopped answering. A browser page cannot close its own tab, so the
/// browser reports <see cref="Supported"/> false and offers to reload instead.
/// </summary>
public interface IClientLifetime
{
    /// <summary>False in the browser: a page does not close its tab.</summary>
    bool Supported { get; }

    /// <summary>Ends the application, as the window's own close button would.</summary>
    void Close();
}

/// <summary>The browser's: nothing to close.</summary>
public sealed class NoClientLifetime : IClientLifetime
{
    public bool Supported => false;

    public void Close() => throw new NotSupportedException();
}
