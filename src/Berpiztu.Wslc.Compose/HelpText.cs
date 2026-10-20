namespace Berpiztu.Wslc.Compose;

/// <summary>What <c>wslc-compose --help</c> prints.</summary>
internal static class HelpText
{
    public const string Text = """
        wslc-compose: Compose files on WSLC, through the WSLC AI Agent.

        Usage: wslc-compose [OPTIONS] COMMAND [SERVICE...]

        The command line is Compose's own: every option Compose has for these commands is taken.
        The ones below are done. Of the rest, some ask for what already happens and change
        nothing; the others are not there yet, and the command says so and goes on without them.

        After build, up, start, stop, restart and logs, the names of services: the command is then
        of those alone. Up and build take with them the services they depend on, and a service
        named is taken whatever its profiles.

        Commands:
          config    Read the Compose file and print it with what each line becomes on WSLC. Nothing is run.
          build     Build the images the file's services are built from. Nothing else is made.
          up        Create and start the project's containers; on a project that exists, only what changed.
          down      Stop and remove the project's containers and its networks.
          start     Start the project's containers, in the order its services depend on each other.
          stop      Stop the project's containers.
          restart   Restart the project's containers that are running.
          logs      Show what the project's containers wrote, as one log; -f to follow it.
          ps        List the project's containers.
          ls        List the projects.
          version   The version of wslc-compose and of the agent it talks to.

        Options:
          -f, --file FILE              The Compose file. Without it: compose.yaml, compose.yml,
                                       docker-compose.yaml or docker-compose.yml in this folder.
          -p, --project-name NAME      The project. Without it: the file's name, then its folder's.
              --project-directory DIR  The folder the file's relative paths start from. Without it: the file's.
              --profile NAME           A profile to take the services of; may be given several times.
              --env-file FILE          A file of NAME=value to read the file's variables from; may be given several times.
              --dry-run                Do nothing, and say what would be done.
              --agent URL              An agent on another machine. Without it: this machine's, which asks for no login.
              --token TOKEN            That agent's API token.
              --ansi auto|always|never Colour on what is printed. Without it: on a terminal, and not in a file or a pipe.
              --progress plain         The build's lines as they come, not drawn.
          -h, --help                   This text.
          -v, --version                The version of wslc-compose.

        Of 'build':
              --no-cache               Build every layer again, taking nothing an earlier build left.
              --pull                   Ask the registry for the base images again.

        Of 'up':
          -d, --detach                 Come back once the containers are up. Without it, up stays and shows their
                                       logs as they are written; Ctrl+C then stops them.
              --build                  Taken and not needed: up always builds the images of the services that
                                       have a build, from the cache where nothing changed.
              --no-start               Create the containers and leave them stopped.
              --remove-orphans         Remove the project's containers whose service the file no longer has.
          -y, --yes                    Answer yes to what up asks: whether this file is to replace another one
                                       the project was saved from. Without a terminal to ask on, up stops there
                                       unless it is given.

        Of 'logs':
          -f, --follow                 Go on showing what is written, until Ctrl+C.
          -n, --tail N                 Begin with the last N lines of each container. Without it: all of them.
          -t, --timestamps             Each line with the moment it was written.
              --since TIME             From that moment on (RFC 3339, or Unix seconds).
              --no-log-prefix          The lines alone, not said by their containers.

        Of 'down':
          -v, --volumes                Remove the project's volumes too, and what they hold.

        Of 'config':
          -q, --quiet                  Read and judge the file, and print nothing of it.
              --services, --profiles, --images, --volumes, --variables
                                       Print that list of the file's instead of the file, a name a line.

        Of 'ps' and 'ls':
          -q, --quiet                  The containers' ids (ps) or the projects' names (ls), one a line.
              --services               The services that have a container (ps).
              --format table|json      As a table, or as JSON.

        Of 'version':
              --short                  The version's number and nothing else.

        The file's ${NAME} variables take the values this terminal has for them, then the ones of --env-file,
        then the ones in the .env beside the file. WSLC_COMPOSE_AGENT and WSLC_COMPOSE_TOKEN stand for
        --agent and --token; NO_COLOR set to anything, for --ansi never.
        """;
}
