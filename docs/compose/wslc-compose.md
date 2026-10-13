# wslc-compose

Compose files on WSLC from the command line. `wslc-compose` is a client of
the agent, as the web UI and the MCP tools are: it reads the command line,
asks the agent over [`/api/v1`](../api-v1.md) and prints what the agent
answers. It decides nothing itself, so a project brought up from a terminal
is the same project the Containers page shows, with its group, its line and
its verbs. How a Compose file becomes containers on WSLC is in
[emulate.md](emulate.md).

Its command line is Compose's own, so a script written for it needs its
first word changed and nothing else:

```
wslc-compose up -d
wslc-compose -f deploy\compose.yaml -p shop up --remove-orphans
wslc-compose ps
wslc-compose down
```

## Where it is

The agent's installer puts `wslc-compose.exe` in the agent's folder
(`%LOCALAPPDATA%\WSLC-AI-Agent`), on the agent's own .NET runtime, and puts
that folder on the user's `PATH`: it is a command in any terminal opened
after the install. The project is `src/Berpiztu.Wslc.Compose`.

A machine with no agent of its own takes the command as one file:
`build-wslc-compose.ps1` writes `dist\wslc-compose.exe`, with the .NET
runtime inside it, to copy wherever it is wanted and aim at another
machine's agent (below).

## Which agent, and who is calling

Without being told, it talks to this user's agent on this machine: the port
the installer wrote (`HKCU\Software\Berpiztu\wslc-agent\Agent`, 8069 unless
changed), on the loopback. The agent asks whoever calls it from its own
machine for no login, so there is nothing to configure and no token to keep.

Another agent is named with `--agent URL` (or `WSLC_COMPOSE_AGENT`) and
given its API token, the one Settings shows, with `--token` (or
`WSLC_COMPOSE_TOKEN`). That agent is on another machine, where a path of
this one means nothing: it is sent the file's text, and what its services
are built from ([below](#building-on-another-machine)). Everything else the
file names by a relative path (its mounts, its `env_file`, its `.env`) is
the agent's machine's, and is read from a folder there:

- the one `--project-directory` names, a folder of the agent's machine. It
  need not be there yet: the folders the file mounts under it are made when
  its containers start;
- without it, the folder the project was last saved with on that agent. A
  project that already lives on a server is brought up again from another
  machine with no folder named: its mounts and its `.env` are the server's
  own, and only the file and the sources travel;
- with neither (a project new to that agent, and no folder named), what
  needs a folder is named as not available.

`build` and `up` say which folder of the agent it is before they do
anything.

### Building on another machine

A `build` and an `up` against an agent on another machine send it the
folders the file's services are built from, and the agent builds there: the
images are made where the containers will run, from the sources on your
desk. It is how a deployment on a server is updated from the machine that
has the new code: the build contexts go, the server's own data folders
stay where they are and are mounted as they were.

```
cd C:\src\shop
wslc-compose --agent https://server.example.com --token … --project-directory D:\apps\shop up -d --build
```

`--project-directory` is needed the first time, to say where on the server
the project lives; after that the agent remembers it.

```
wslc-compose --agent https://server.example.com --token … up -d --build
▸ Sending build context ./app (12.4 MB, 318 files)
```

Each context (`build: ./app`, or `build.context`) is packed as a tar
archive, as `docker build` packs it: without what its `.dockerignore`
leaves out, the Dockerfile and the `.dockerignore` themselves always in.
The agent keeps the archive and hands it to `wslc build -`, which reads a
context from its standard input since **wslc 3.0.3**; nothing is unpacked
on the agent's disk. Only the contexts of the services the command takes
are sent, and one the agent already has is not sent again: it is known by
its bytes, so a folder that did not change costs one question. The agent
lets go of a context nobody has used in a week.

What it asks for:

- **wslc 3.0.3 or later on the agent's machine.** An older one cannot read
  a context from its standard input, and the build says so, with what to
  update.
- The Dockerfile inside its context, as it nearly always is: one named
  outside it (`dockerfile: ../Dockerfile`) is not in what is sent.
- An agent of API level 18 or later. An older one names the `build` as not
  available, as before.

`config` and `--dry-run` send nothing: they say the `build` is one whose
context the client sends when it builds.

A development agent is reached the same way:
`wslc-compose --agent http://127.0.0.1:8070 ps`.

## Commands

| Command | What it does | The agent's call |
|---|---|---|
| `config` | Prints the file, each line followed by what the agent makes of it as a comment (`# converted: …`, `# NOT SUPPORTED: …`), so the output is still the file. Nothing is run. `-q` prints nothing of the file, as Compose's does; `--services`, `--profiles`, `--images`, `--volumes` and `--variables` print that list instead, a name a line | `POST /projects/plan` |
| `build` | Builds the images the file's services are built from, one after another, and makes nothing else: no network, no volume, no container. Services built from the same context and Dockerfile are built once, and the image takes each one's name. Followed line by line, with what the build prints. `--no-cache` builds every layer again; `--pull` asks the registry for the base images again | `POST /projects/build`, `GET /projects/jobs/{id}` |
| `up` | Saves the file as a project and starts it: on a project that exists, only what changed is recreated. Followed line by line until it ends. `--no-start` creates the containers and leaves them stopped; `--remove-orphans` removes the project's containers whose service the file no longer has (they are named, and kept, without it); Without `-d` it then stays with the containers it brought up and shows their logs as they are written, each line said by its container, until they have all ended or Ctrl+C, which stops them; with `-d` it comes back once they are up. When one of them could not start, it stays with the ones that run all the same, and ends as the failure it was. `--build` is taken and not needed: it always builds the images of the services that have a `build`, from the cache where nothing changed | `POST /projects/preview`, `/projects/up`, `GET /projects/jobs/{id}` |
| `down` | Stops and removes the project's containers and its networks; `-v` its volumes too | `POST /projects/{name}/down` |
| `start`, `stop`, `restart` | The verb on the project's containers, in the order its services depend on each other. A restart is of the ones that run | `POST /projects/{name}/start`, `/stop`, `/restart` |
| `logs` | What the project's containers wrote, as one log in the order it was written, each line said by its container, the names in a column and each in its colour. `-f` goes on showing what is written, until Ctrl+C; `-n N` begins with the last N lines of each container (all of them without it); `-t` adds the moment of each line; `--since` reads from a moment on; `--no-log-prefix` prints the lines alone. With services' names, theirs alone. A container that could not start says so there | `GET /projects/{name}/logs` |
| `ps` | The project's containers: name, image, service, status, ports | `GET /containers` |
| `ls` | The projects, each with how many of its containers are in each state | `GET /containers` |
| `version` | The version of `wslc-compose` and of the agent it talks to | `GET /health` |

**Services by name.** `build`, `up`, `start`, `stop` and `restart` take the
names of services after them, and are then of those alone:

```
wslc-compose up -d web
wslc-compose stop web worker
```

`up` and `build` take with them the services the named ones depend on, and
a service that is named is taken whatever its profiles: with a file whose
default service and whose `web` both want port 5000, `up web` starts `web`
and nothing else, where `--profile web-only up` starts both, as Compose
does. The containers of the services left out are not touched, and are not
orphans. The services an `up` took are kept with the project by name, as
its profiles are, whether they were named or not (a plain `up` of a file
whose only service without a profile is `bot` keeps it with `bot`), and
each `up` adds to them: `up web` and later `up` leave the project kept with
`web` and with what the plain `up` brought. A `start` of
the whole project afterwards starts those, and the window shows them
ticked. (The window's own Save sets the choice exactly as it is ticked.) `start`, `stop` and `restart`
with names go through the containers of the
named services with each one's own verb, so the agent's rules stand: one
whose service depends on a stopped one is not started. A name the file does
not have is refused. Against an agent older than this (API level 10) a
`build` or an `up` with names is refused too: that agent would not see the
names and would bring up the whole project.

Not there yet, and said so when asked for: `pull`, `exec` and the rest of
Compose's commands, and services' names after `down`, `ps` and `config`.
The whole list of what is missing is at the end:
[Not there yet](#not-there-yet).

## Options

| Option | |
|---|---|
| `-f`, `--file FILE` | The Compose file. Without it, the first of `compose.yaml`, `compose.yml`, `docker-compose.yaml`, `docker-compose.yml` in the current folder. One file: several are not merged yet |
| `-p`, `--project-name NAME` | The project. Without it, the file's `name`, then its folder's. With it and no file at hand, the commands that need no file (`down`, `start`, `stop`, `restart`, `ps`) work from any folder |
| `--project-directory DIR` | The folder the file's relative paths start from; the file's own without it |
| `--profile NAME` | A profile to take the services of; may be given several times |
| `--agent URL`, `--token TOKEN` | Another agent, and its API token |
| `--ansi auto\|always\|never` | Colour on what is printed. Without it, on a terminal and not in a file or a pipe; `NO_COLOR` set to anything is `never` |

| `--env-file FILE` | A file of `NAME=value` to read the file's variables from; may be given several times, a later one winning |
| `--dry-run` | Nothing is done, and what would be is said, a line each (`DRY-RUN  would recreate container web`) |
| `--progress plain` | A build's lines as `wslc` prints them, not drawn |

The file's `${NAME}` variables take the values the terminal has for them,
then the ones of `--env-file`, then the ones in the `.env` beside the file.

### Every option Compose has

The command line is Compose's own, whole: a script written for Compose has
its first word changed and runs. So every option Compose has for these
commands is taken, wherever it stands, and none of them is an error. Each
gets one of three things:

- **Done**: the ones in the tables above, and for the lists `ps -q`,
  `ps --services`, `ps --format json`, `ls -q`, `ls --format json`,
  `config --services`, `--profiles`, `--images`, `--volumes`,
  `--variables`, `version --short`. `config -q` is Compose's: the file is
  read and judged and nothing of it is printed.
- **Taken, with nothing to do**, since what it asks for is what happens
  anyway or is about a choice this command does not offer: `up --build`,
  `--quiet-pull`, `--attach`, `-y`; `build
  -q`, `--with-dependencies`; `ps -a`, `--no-trunc`; `--compatibility`,
  `--parallel`.
- **Not there yet**: the command says so on the error stream (`--wait is
  not there yet in wslc-compose up: it goes on without it`) and goes on, so
  the script reaches its end. Among them `up --force-recreate`,
  `--no-recreate`, `--no-build`, `--no-deps`, `--pull POLICY`, `--scale`,
  `--wait`, `-t`; `build --build-arg`, `--push`; `down --rmi`,
  `--remove-orphans`; `stop -t`; `ps --filter`, `--status`; `config
  --format`, `-o`; a template in `--format`.

What is refused is what Compose refuses: an option it does not have, or one
of another command (`ps --no-cache`). `--dry-run` is done by every command
that changes something, never let go by: an option that makes a command
harmless cannot be one that is ignored.

## What it answers

Text for a person on the output, warnings and failures on the error stream,
and an exit code for a script. On a terminal it is in colour, with a mark
before what matters: `▸` a step of the work, `✔` something done, `⚠` a
warning, `✖` a failure or what the file asks for and will not get; the
command a step ran in grey, a table's state in the colour of the state, and
`config` with the file's keys and each line's verdict in theirs. Sent to a
file or to another program, the same words with no colour and no mark.

A build, in a `build` or in an `up`, is drawn on a terminal as Compose
draws it: on the first line how long the builds have taken and how many
steps are done; then a line a step, said by its service (`=> [backend base
2/12] RUN …`); the step under way in bold with its time running and the
last lines it printed in grey under it; a finished one in blue with the
time it took, `CACHED` before one taken from an earlier build; a failed one
in red, followed by what it printed; and at the end each image with its
tick. While the builds last they are a piece of the screen drawn again in
its place, the first line and under it the last ones the window has room
for; once they are over the whole of it is printed, to scroll back through. It is made from what
`wslc build --progress plain` prints, which says when a step begins and
not when it ends or how long it took: the times are counted by
`wslc-compose` between one step's line and the next, as it reads the job's
log four times a second. Sent to a file, the build's lines go as `wslc`
printed them.

The exit code: `0` done, `1` the agent refused, failed or
did not answer, `2` the command line asks for something it does not have,
`130` stopped with Ctrl+C.

Ctrl+C during an `up` cancels the work on the agent, as the cross of the
project's row does, and waits for the agent to say it stopped; a second one
comes back at once and leaves it to the agent. Once the containers are up
and their logs are being shown (an `up` without `-d`), Ctrl+C stops the
containers, as it does under Compose, and a second one leaves them running
and comes back. During a `logs -f` it only ends the following.

An `up` without `-d` that could not start every container says which one
and why, and then shows the logs of the ones that are running, the failed
one's line among them, as it would have had they all come up. It comes back
only when none of them runs. Its exit code is still `1` when those
containers end by themselves, and `130` when Ctrl+C stopped them. A project that fails to come
up leaves no row behind on the Containers page: its whole log was printed
where it was asked for.

## Not there yet

Compose on WSLC is new, and this is what it still lacks. We are working on
it; none of it fails in silence, each is said where it is met.

**Commands.** `pull`, `push`, `exec`, `run`, `kill`, `rm`, `create`,
`pause`, `unpause`, `top`, `events`, `images`, `port` and `cp`: the command
answers that it has none yet (exit code `2`). Services' names after `down`,
`ps` and `config`.

**Options.** The ones listed above as *Not there yet*
([Every option Compose has](#every-option-compose-has)): they are taken,
said on the error stream, and the command goes on without them.

**Several files as one.** More than one `-f`, a `compose.override.yaml`
beside the file (the plan says it is there and was not read), `include` and
`extends`. One file is read, whole.

**In the file.** `secrets` and `configs`, `pull_policy`, and a `volumes`
entry of type `tmpfs` in the long form (the short `tmpfs:` key is taken).
`config` names each of them on its line, as it does everything else that is
not taken.

**From an AI agent.** Over MCP a Compose file can be read and judged
(`parse_compose`); bringing a project up or down (`compose_up`,
`compose_down`, `list_projects`, behind the approval gate) is not written
yet.

**On the Containers page.** A project's group can be cut by the pager, the
list sorted by a column is flat, the group's tick does not tick its
containers, and there is no Export of a project.

What will not come from us is what `wslc run` has no flag for
(`privileged`, `cap_add`, `network_mode: host` and the others): the list,
and why, is in [emulate.md](emulate.md#cannot-be-done). `config` names them
before anything runs.
