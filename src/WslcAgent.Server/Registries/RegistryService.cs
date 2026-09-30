using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Registries;

/// <summary>
/// <c>wslc login</c> and <c>wslc logout</c>.
/// The password goes through
/// <c>--password-stdin</c>, so it never shows in the command line that CLI
/// Activity and the agent log record.
/// </summary>
public sealed class RegistryService(IWslcRunner wslc)
{
    public Task LoginAsync(RegistryLoginRequest request, CancellationToken cancellationToken = default)
    {
        var (args, password) = LoginArgs(request);
        return wslc.RunAsync(args, cancellationToken: cancellationToken, standardInput: password);
    }

    public Task LogoutAsync(RegistryLogoutRequest request, CancellationToken cancellationToken = default) =>
        wslc.RunAsync(WithServer(["logout"], request.Server), cancellationToken: cancellationToken);

    /// <summary><c>login [--username U] [--password-stdin] [SERVER]</c> and what to write to stdin (null without a password).</summary>
    internal static (List<string> Args, string? Password) LoginArgs(RegistryLoginRequest request)
    {
        var args = new List<string> { "login" }.Option("--username", request.Username);
        var password = string.IsNullOrEmpty(request.Password) ? null : request.Password;
        args.Flag("--password-stdin", password is not null);
        return (WithServer(args, request.Server), password);
    }

    private static List<string> WithServer(List<string> args, string server)
    {
        if (!string.IsNullOrWhiteSpace(server))
        {
            args.Add(WslcArgs.Require(server, "registry server"));
        }

        return args;
    }
}
