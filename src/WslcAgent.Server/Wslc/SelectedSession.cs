using Microsoft.Extensions.Options;

namespace WslcAgent.Server.Wslc;

/// <summary>The session every <c>wslc</c> command targets; changeable at run time.</summary>
public interface ISelectedSession
{
    /// <summary>
    /// The session's name, never empty: choosing none is choosing the CLI's own
    /// store for this user (<see cref="SessionStores.Default"/>), which is a
    /// session with a name like any other. What makes it the default is how it
    /// travels on the command line, which is <see cref="WslcRunner"/>'s business.
    /// </summary>
    string Name { get; set; }
}

/// <summary>Starts from <see cref="WslcOptions.SelectedSession"/>; persisting a change is the settings slice's job.</summary>
public sealed class SelectedSession(IOptions<WslcOptions> options) : ISelectedSession
{
    private string _name = Named(options.Value.SelectedSession);

    public string Name
    {
        get => _name;
        set => _name = Named(value);
    }

    /// <summary>No session chosen is the CLI's own store, by its name.</summary>
    private static string Named(string? value) =>
        value?.Trim() is { Length: > 0 } name ? name : SessionStores.Default;
}
