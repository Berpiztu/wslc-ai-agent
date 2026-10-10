# Compose files on WSLC: a plan

`wslc` has no Compose. Its commands stop at one container, one network, one
volume (`wslc --help`, 3.0.2), and Microsoft has only announced `compose up`
as planned. Most real applications are published as a `compose.yaml`, so
today they cannot be brought up on WSLC without rewriting them by hand.

This plan is for the agent to read a Compose file and bring it up itself,
with the pieces it already has. Nothing here is built yet.

## The idea

A Compose file is a description, not a program: services, networks and
volumes, and the order to start them in. The agent already creates each of
those one at a time. So the work is a **reader** that turns the file into
the requests the agent already takes, and a **runner** that sends them in
the right order and remembers they belong together.

The Run form's Fill bar is the precedent: `RunCommandLine` reads a
`docker run` line into a `ContainerLaunchRequest`, names what it cannot use
and never drops a flag in silence. A Compose file is the same reading, for
several containers at once.

## What a Compose file asks for, and what answers it

### Already there

| Compose | The agent today | `wslc` |
|---|---|---|
| `image`, `command`, `entrypoint`, `user`, `working_dir` | `ContainerLaunchRequest` | `run` |
| `environment` | `Env` | `--env` |
| `ports` | `Publish` | `--publish` |
| `volumes` (bind mounts and named volumes, `:ro`) | `Volumes` | `--volume` |
| `networks`, with a fixed address | `Network`, `Ip`, `ConnectNetworks` | `--network`, `--ip`, `network connect` |
| A service reached by its name | `NetworkAliases` | `--network-alias` |
| `healthcheck` | `HealthCmd` and its four times, `NoHealthcheck` | `--health-*` |
| `stop_grace_period` | `StopTimeout` | `--stop-timeout` |
| `mem_limit`, `cpus`, `deploy.resources.limits` | `Memory`, `Cpus` | `--memory`, `--cpus` |
| `gpus: all` | `Gpus` | `--gpus all` |
| `restart` | The agent's restart policies (`wslc` has no `--restart`) | none |
| `build` (context, dockerfile, args, target) | Image build | `image build` |
| Top-level `networks` (subnet, gateway, internal) | Network create | `network create` |
| Top-level `volumes` (and `vhd` with a size) | Volume create | `volume create` |
| An image that is not local | Pulled first, as a job with progress | `image pull` |
| `host.docker.internal` in `extra_hosts` | Not needed: WSLC resolves it, and `host.wslc.internal`, with no flag | none |

### In the launch form since Compose asked for them

`wslc run` takes these, and the launch request, the Run form and the pasted
`docker run` line take them now too, so a service that uses them is edited
in the Run form like any other container.

| Compose | The launch request | `wslc` |
|---|---|---|
| `labels` | `Labels` | `--label` |
| `hostname`, `domainname` | `Hostname`, `Domainname` | `--hostname`, `--domainname` |
| `dns`, `dns_search`, `dns_opt` | `Dns`, `DnsSearch`, `DnsOptions` | `--dns`, `--dns-search`, `--dns-option` |
| `tmpfs` | `Tmpfs` | `--tmpfs` |
| `shm_size` | `ShmSize` | `--shm-size` |
| `ulimits` | `Ulimits` | `--ulimit` |
| `stop_signal` | `StopSignal` | `--stop-signal` |

`wslc container inspect` says back the labels and the ulimits alone (seen in
wslc 3.0.2). The others the agent writes on the container itself, in the
label `ai.berpiztu.wslc.launch`, and reads from there for View & edit and
for every recreate: nothing set at launch is lost when a container is
edited.

### `wslc` can, the launch request cannot yet

| Compose | `wslc` |
|---|---|
| `volumes` in the long form, of type `tmpfs` | `--mount`, `--tmpfs` |
| `pull_policy` | `--pull` (the agent pulls an image that is not local itself, before the run) |

### New work

| Compose | What has to be written |
|---|---|
| The file itself | A YAML reader (YamlDotNet, MIT) and the Compose model |
| `${VAR}`, `${VAR:-default}`, `.env` | Interpolation before the model is read |
| `env_file` | Read by the agent into `Env` (it knows the folder the file is in) |
| Relative paths (`./data:/data`, `build: .`) | Resolved against the Compose file's folder on the agent's machine |
| `depends_on`, with `service_healthy` and `service_completed_successfully` | Start order, and waiting on `HealthStatus` or the exit code |
| The project | A name, and the containers, networks and volumes that belong to it |
| `up` on a project that already runs | Telling which services changed |
| `profiles` | Services left out unless their profile is asked for |
| `secrets`, `configs` from files | Read-only bind mounts at the path Compose gives them |

### Cannot be done

`wslc run` has no flag for these (`wslc container run --help`), so a file
that uses them is read, and each is named as not supported, as the Fill bar
does:

`privileged`, `cap_add`, `cap_drop`, `devices`, `read_only`, `sysctls`,
`security_opt`, `pid`, `ipc`, `userns_mode`, `init`, `logging`,
`network_mode: host` or `service:…`, `extra_hosts` with an address,
`deploy.replicas` above one, and Swarm's `deploy` keys.

Whether the application still works without them is the user's call: the
plan shows the list before anything runs.

## How

### 1. The reader

In the agent (`WslcAgent.Server/Projects/ComposeReader.cs`), and only there:
a file on the agent's machine is read with the `.env` and the `env_file`s
beside it, which no client can reach, and the YAML library stays out of the
browser and the phone. Every client asks the agent for the plan, a pasted
file included, so all of them are told the same.

```text
ComposeReader.Read(yaml, source) -> ComposePlan
  Name          the project
  File          the file read, empty for a pasted one
  Services[]    name, ContainerLaunchRequest, DependsOn[], Build?, Profiles[], Enabled
  Networks[]    key, name, subnet, gateway, internal, external
  Volumes[]     key, name, driver, options, external
  Unsupported[] "web: privileged", "db: labels (not yet)"...
  NotNeeded[]   "web: extra_hosts host.docker.internal:host-gateway"
  Warnings[]    "The variable TAG is not set: read as empty."
```

Every key of a service the reader does not take is named in `Unsupported`,
so a key nobody thought of is said, not lost. `(not yet)` marks what only
the agent does not send yet (the keys of phase 4).

The plan also carries the file itself, `Lines[]`, each line with its
verdict: taken as written, converted (and into what), not supported (and
why), not needed, or to look at. The reader keeps the line every value was
written on, a merged one (`<<`) included, so the plan screen shows the file
in one column and what becomes of each line in the other, and is read as the
file is.

Several files (`compose.yaml` plus `compose.override.yaml`) merge before
parsing, as Compose does. The reader is where a mistake goes unseen, so it
is the part with tests: real files from well-known projects, read and
compared with what they should produce.

### 2. What belongs together

Three labels on every container, network and volume a project creates.
Checked on wslc 3.0.2: labels are kept, `inspect` and `list --format json`
return them, and `list --filter label=…` selects by them.

```text
ai.berpiztu.wslc.project = shop
ai.berpiztu.wslc.service = web
ai.berpiztu.wslc.config  = <hash of the service as read>
```

So the project is on the containers themselves: an agent reinstalled, or
another agent on the same session, still sees it.

The Compose file is kept with the project, because the project is that
file: the group on the screen is the file brought up. A label cannot hold
it, so the agent keeps it in its data folder, one folder per project
(`projects/<name>/`): the Compose file as it was last brought up, the
`.env` and the variables given with it, the profiles asked for, and the
path it came from when it came from one. The copy is what **View** shows
and what the `config` labels were worked out from, so what the screen
shows is always what is running, whatever has happened to the file on
disk since.

A group whose containers are there and whose folder is not (the data
folder was lost, or the containers were brought up by another agent) is
still a group, drawn from its labels: it can be stopped, started and taken
down, and it says that its file is not here, so it cannot be viewed or
brought up again until one is given.

Names follow Compose so the files people already have keep working:
container `shop-web-1`, network `shop_default`, volume `shop_data`. Every
service joins the project's default network with its service name as alias,
which is how `db:5432` resolves from `web`.

### 3. Up

One job of the agent, like a run or a pull: it has progress, a console and
a cancel, every client sees it, and it goes on with the browser closed.

1. Read the file and build the plan. Stop here if the file cannot be read.
2. Run every service through the checks the Run form uses (`ILaunchChecks`):
   names in use, ports taken, a command on another port than the published
   one. Errors stop it; warnings and the not-supported list are shown and
   asked about.
3. Create the networks and volumes that are missing.
4. Pull or build the images, several at a time.
5. Start the services in `depends_on` order, each wave waiting for what it
   depends on: started, healthy, or finished with exit code 0.
6. Enrol each service's restart policy with the agent.

On a project that already exists, the `config` label says which services
changed. Unchanged ones are left alone. A changed one goes through the
recreate the agent already has (`ContainerRecreations`): rehearsed under
another name first, and the old container put back if the new one does not
start. Containers of the project that the file no longer names are listed
and removed only when asked.

### 4. The rest of the life cycle

| Verb | What it does |
|---|---|
| `down` | Stops and removes the project's containers and its networks; its volumes only when asked |
| `start`, `stop`, `restart` | The same verbs on every service, in order |
| `ps` | The project's containers with state, health and ports |
| `logs` | The services' logs in one view, each line with its service |
| `pull`, `build` | The images alone, without starting anything |

### 5. API, MCP and UI

- **API**: `/api/v1/projects`: list, plan (read a file and answer the
  plan, nothing run), up, down, start, stop, restart, logs. In
  `docs/api-v1.md`, and `ApiCompatibility.Level` raised.
- **MCP**: `parse_compose` (read only, the plan), `compose_up`,
  `compose_down`, `list_projects`, `project_logs`. `compose_down` and an
  `up` that would recreate or remove a container are destructive: off until
  switched on, then through the approval gate, as remove and prune are.
- **UI**: no page of its own. A project is shown where its containers
  already are, on the Containers page, as a group
  (see [The project on the Containers page](#the-project-on-the-containers-page)).
  New project: a verb of that page; pick a Compose file on the agent's
  machine, or paste one; the plan is shown before **Up**.
- **Skill**: a section so an assistant handed a Compose file uses
  `parse_compose` first and reports the not-supported list.

### The project on the Containers page

A project is a group of containers, and it is drawn as one in both views.
The containers inside it are the ones the page already shows, with every
verb they have today.

**Cards.** A project is a card of its own followed by its containers'
cards, each in a cell of the cards area as any other card is, so no row is
left half empty; a line runs round them as one, from the project's card to
its last container's. The project's card carries its name, how many of its
services run, and the project's verbs (start, stop, restart, the file,
down). A container's card is the same card in or out of a project. While an `up` is on its way, the header's avatar is the ring the
lists already use, with its output and its cancel.

**Rows.** A project is one row, the grouper: its name, its services
running out of the total, the CPU and memory of its containers added up,
and the project's verbs. Its **+** opens the rows of its containers under
it, indented, and they are ordinary container rows: the same columns, the
same actions, the same tick. They are rows of the same table, not a table
inside the row, so the columns stay aligned and resizing or reordering one
moves them all.

**The grouper is the Compose file.** What a container's View & edit is to
a container, the group's is to the project, from its row or its card's
header:

- **View** opens the Compose file as it was last brought up, with the
  plan read from it beside it: the services, what is not supported, what
  is not needed. When the file came from a path and the one on disk is no
  longer the same, it says so and shows what changed.
- **Edit** is the same window with the file open to change. Saving shows
  the new plan, which services it would recreate and which it would leave,
  and brings it up: the same `up` as before, rehearsal and way back
  included. A file that came from a path is written back there as well.
- **Export** hands the file out, with its `.env`, to take the project to
  another machine.

What follows from that, in both views:

- Containers that belong to no project stand as they do today, beside the
  groups.
- A group is never cut by the pager: a page is counted in groups and loose
  containers, not in containers.
- Sorting orders the groups by their own value (name, added-up CPU) and the
  containers inside each group by theirs.
- A search that finds a container inside a group shows the group open on
  what matched.
- Ticking the grouper ticks its containers; the bulk bar acts on them.
- Open or closed is remembered per group and per device, as table or cards
  is.

## Phases

Each one is usable on its own and ends with something to try.

1. **Read and show.** *Written, on the branch `emulate-compose`.* The
   reader, its tests, `POST /api/v1/projects/plan` and `parse_compose`. On
   screen, the **Compose** verb of the Containers page opens a window built
   as the Run form is. Its one field, **Docker Compose file, or paste**, is
   one line: its button opens the system's Open dialog and leaves the
   file's path there, or a path is pasted, or the Compose file itself is. Under it, **Context** (the folder its
   relative paths start from) and, beside it, **Check Compose file**: nothing
   is read until it is pressed.
   Under the field, the file as the tree it is (MudBlazor's tree view): its services, networks and
   volumes as branches to open and close, and on each branch's line what it
   becomes. Nothing is started. It already answers "will this file run on
   WSLC, and what would
   be lost?". At the agent's machine the Load button has the agent open
   Windows' Open dialog (`POST /api/v1/projects/open`), so the file comes
   with its path and is read with its folder. From another device the
   browser opens the dialog and the file arrives as its text, without the
   folder it was in: relative paths, `build` and `env_file` cannot be
   resolved from it, and are named as such. Given a folder alone (the API,
   `parse_compose`), the standard name is read and the folder's other
   Compose files are named. Not read yet: a
   `compose.override.yaml` beside the file (the plan says so), `include` and
   `extends`.
2. **Save and Run.** *Written, on the branch `emulate-compose`.* The
   Compose window's rail has the Run form's two verbs. `ProjectRunner`
   (`POST /api/v1/projects/preview`, `/up`, `GET /projects/jobs/{id}`) does
   the work as a job of the agent: labels, networks, volumes, `build`, a
   container a service in start order, `service_healthy` and
   `service_completed_successfully`, restart policies, the `config` label
   and the recreate of what changed, orphans removed only after asking, and
   the file kept with the project. The window closes as soon as the agent
   has the job, as a Run's does: each container the project makes is a Run
   of its own, so the Containers table shows it on its way with its ring,
   its pull and its cancel, and a failed one keeps its row and its settings
   to open in the form; the title bar's activity line says how the whole
   goes (`ProjectActivity`). The project itself is a row of that table while
   it is saved (`GET /containers/launches` lists it, marked with its job's
   id): its name, how far it has got, its cancel, and its log, which holds
   every step and what the builds printed, drawn by the container's own
   logs view (`LogsView` in `ProjectLogDialog`): the lines coloured by
   level, and its rail. A failed one
   keeps its row and its log until dismissed. It is a row that stands for
   no container: the place the project's group takes in phase 3. The file's
   `${NAME}` variables are listed under the conversion (**Variables**), each
   with the value it was read as, to set before the save.
3. **The project in the list.** *Written, on the branch `emulate-compose`.*
   The group on the Containers page, in rows and in cards
   (`Containers.razor`: a head row a project, its containers under it while
   it is open, kept together by the place each is given; in cards the
   project's own card and then its containers', in the cells any card takes,
   with a line round them as one; `ProjectActions`),
   open or closed remembered per project and per device (`ProjectGroups`);
   the verbs on the whole project (`ProjectLifecycle`: `POST
   /projects/{name}/start`, `/stop`, `/restart`, `/down`); and the project's
   file opened from its row in the Compose window (`GET /projects/{name}`,
   `ProjectStore`), where a save applies it again. A container says its
   project and its service in the list (`ContainerSummary.Project`,
   `.Service`, from its labels). Left of what the section above describes:
   a group never cut by the pager, groups sorted as groups (sorted by a
   column, the list is flat), the grouper's tick ticking its containers,
   Export, and the services' logs in one view.
4. **What is left of the project.** The MCP tools (`compose_up`,
   `compose_down`, `list_projects`, behind the approval gate), and each
   service of the Compose window's table opened in the Run form before the
   project is saved.
   A mount WSLC cannot make as written is converted by the reader
   (`ComposeReader.Unlinked`): WSLC does not follow a Windows junction or
   symbolic link inside a mounted folder, so a mount the file puts over one
   fails at start (`openat2 …: operation not permitted`). The link is
   mounted from its real folder, and the folder that holds it as its
   entries, one mount each, down to the link. The line says so, and that an
   entry added to that folder later needs the project saved again.
5. **What is left.** Long mounts and `pull_policy`, then `profiles`, and
   `secrets` and `configs` as mounts. (The flags the request lacked, labels,
   host and domain names, DNS, tmpfs, shm size, ulimits and stop signal, are
   in the launch form: written with phase 1.)

## Decisions to take

1. **Where the file comes from.** The agent always keeps its own copy with
   the project (above). What is open is where a file may come from. A path
   on the agent's machine keeps relative paths and `build` working, and is
   the case Compose was made for. A pasted or uploaded file has no folder:
   bind mounts with relative paths and `build` cannot work from it.
   Proposed: both, with those two things named as not available for a file
   that has no folder.
2. **Its name in the product.** "Projects" (what Compose calls them), with
   "Compose file" for the file. Nothing is named after Docker.
3. **When Microsoft ships `compose up`.** The reader, the plan, the labels
   and the screens stay. Only the runner could hand the work to `wslc`, if
   its result can still be labelled and followed. The runner sits behind
   one interface for that reason.
4. **Sessions.** A project belongs to the session it was brought up in, as
   its containers do. Proposed: the groups follow the selected
   session, as every other list.

## What it changes outside the code

The comparison with Docker Desktop
([wslc-ai-agent-vs-docker-desktop.md](../features/wslc-ai-agent-vs-docker-desktop.md))
names Compose as the biggest gap. After phase 2 that line reads "Compose
files, read and run by the agent".
