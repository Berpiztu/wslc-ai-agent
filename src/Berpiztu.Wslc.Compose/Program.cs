using System.Reflection;
using System.Text;
using WslcAgent.ApiClient;

namespace Berpiztu.Wslc.Compose;

/// <summary>
/// wslc-compose: reads the command line, hands the command to the agent and
/// says how it went, as a line of text and as the exit code a script reads.
/// </summary>
internal static class Program
{
    /// <summary>Done.</summary>
    public const int Ok = 0;

    /// <summary>The agent refused or failed, or could not be reached.</summary>
    public const int Failed = 1;

    /// <summary>The command line asks for something this command does not have.</summary>
    public const int Usage = 2;

    /// <summary>Stopped with Ctrl+C: 128 plus the signal's number, as a shell says it.</summary>
    public const int Interrupted = 130;

    public static string Version =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    private static async Task<int> Main(string[] args)
    {
        // What a build prints goes through as it is, accents and box lines included.
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var arguments = Arguments.Parse(args);
            Terminal.Setup(arguments.Ansi);
            // -v before any command is Compose's short way of asking for the version; after down it is the volumes.
            if (arguments.Has(Arguments.VersionFlag) || (arguments.Command.Length == 0 && arguments.Has(Arguments.Volumes)))
            {
                Terminal.Line($"wslc-compose {Version}");
                return Ok;
            }

            if (arguments.Has(Arguments.Help) || arguments.Command is "" or "help")
            {
                Terminal.Line(HelpText.Text);
                return Ok;
            }

            arguments.Check();

            // Compose has these options and this command does not do them yet: it says so and
            // goes on, so a script written for Compose runs to its end instead of stopping here.
            foreach (var option in arguments.NotThereYet)
            {
                Terminal.Warning($"--{option} is not there yet in wslc-compose {arguments.Command}: it goes on without it.");
            }

            return await RunAsync(arguments);
        }
        catch (UsageException ex)
        {
            Terminal.Error(ex.Message);
            Terminal.Aside("wslc-compose --help says what it takes.");
            return Usage;
        }
    }

    private static async Task<int> RunAsync(Arguments arguments)
    {
        using var interrupt = new Interrupt();
        using var agent = AgentConnection.Open(arguments);
        try
        {
            return await RunAsync(arguments, agent, interrupt);
        }
        finally
        {
            // Whatever it printed as a warning or an error, in the agent's log too.
            await agent.ReportAsync(Terminal.TakeSaid(), $"wslc-compose {arguments.Command}");
        }
    }

    private static async Task<int> RunAsync(Arguments arguments, AgentConnection agent, Interrupt interrupt)
    {
        try
        {
            await agent.CheckAsync(interrupt.Token);
            return await new Commands(agent, arguments, interrupt).RunAsync();
        }
        catch (Exception ex) when (ex.IsSignInRequired())
        {
            Terminal.Error($"The agent at {agent.Address} asks who is calling. Give its API token (Settings shows it): --token, or {AgentConnection.TokenVariable}.");
            return Failed;
        }
        catch (AgentApiException ex)
        {
            Terminal.Error(ex.Message);
            return Failed;
        }
        catch (CommandFailed ex)
        {
            Terminal.Error(ex.Message);
            return Failed;
        }
        catch (HttpRequestException ex)
        {
            Terminal.Error($"The agent does not answer at {agent.Address}. Is WSLC AI Agent running?");
            Terminal.Aside(ex.Message);
            return Failed;
        }
        catch (OperationCanceledException) when (interrupt.Token.IsCancellationRequested)
        {
            return Interrupted;
        }
        catch (OperationCanceledException)
        {
            // Nobody stopped it: the call ran out of the time a call is given.
            Terminal.Error($"The agent at {agent.Address} took too long to answer.");
            return Failed;
        }
    }
}

/// <summary>The command line asks for something this command does not have; the message says what.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>The command could not be done, for a reason that is not the agent's own answer; the message says why.</summary>
internal sealed class CommandFailed(string message) : Exception(message);
