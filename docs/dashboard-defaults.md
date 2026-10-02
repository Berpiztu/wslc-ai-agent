# Dashboard defaults, their updates and exporting a dashboard

Status: **built, not released yet** (agreed and written 1 October 2026).
Where the files are published between releases is still open: see
[Open points](#open-points); for now they ride on the latest release.

## How it is built

| Piece | Where |
|---|---|
| The file format (`kind`, `version`, `minAgentVersion`, `from`, `created`, `content`) | `src/WslcAgent.Server/Overview/DefaultsFile.cs` |
| The shipped versions, raised by hand when a file changes | `src/WslcAgent.Server/Overview/defaults-versions.json`, embedded |
| Status, loading, reset, import and export | `DashboardDefaults.cs`, endpoints in `HomeEndpoints.cs` ([api-v1.md](api-v1.md)) |
| Files loaded, kept through updates | `%LOCALAPPDATA%\WSLC-AI-Agent\data\loaded-defaults\` |
| Every state the dashboard was saved in | `%LOCALAPPDATA%\WSLC-AI-Agent\data\dashboard-history\`, one file each, all kept (`DashboardHistory.cs`) |
| What differs, JSON against JSON | `DashboardDiff.cs` |
| The dashboard's **Tools** verb | page `/dashboard/tools`: Berpiztu.Dashboard's `Tools/DashboardTools.razor` over `IDashboardFiles`, which `WslcAgent.UI/Dashboard/AgentDashboardFiles.cs` implements with the agent's API: load, export and reset |
| Designing how objects are born | **Design objects**, at the top of the toolbox in design, on any agent: opens `/dashboard/objects` in the view being designed; Save writes the repository on a development agent, the agent's own file on an installed one |
| A release carries both files | `deploy-release.ps1` (`Write-WslcAgentDefaultsFiles` in `packaging/Packaging.ps1`) |
| Publishing them between releases | `publish-defaults.ps1`: uploads the files whose version went up to the latest release |
| The update line | `install.ps1` puts a newer file in the package folder, even when the agent is up to date |

A file is the content the agent keeps today wrapped, never changed, so
nothing that reads the dashboard or the objects' defaults changes:

```json
{
  "kind": "wslc-dashboard",
  "version": 3,
  "minAgentVersion": "1.0.12",
  "from": "",
  "created": "2026-10-01T09:00:00-05:00",
  "content": { "...": "dashboard-v2.5.default.json as it is" }
}
```

**Two numbers, the origin and the revision.** The default dashboard has one
version (`defaults-versions.json`), which Save as default on a development
agent raises by itself. Every state the user's dashboard is saved in is kept
(`dashboard-history/`, all of them for now) with its origin — that version,
given by the installation or by a file imported — and the user's revision
within it: v2.0 is version 2 as it came, v2.3 the third save since. Loading
from the release or a file starts a new origin at revision 0; Restore brings
any state back under its own name. Nothing is replaced unrecorded: before a
reset, a load, a restore or a save, the dashboard it replaces is recorded
first ("Kept before: …") whenever the history does not already hold it as
its newest state — one saved before the history existed, above all. A reset
to the release once wrote over a whole dashboard that was nowhere else.

**The resources travel by name.** An object keeps only its resource's uid,
a number each agent gives its containers, images, volumes and networks in
its own order: the same container is 25 on one machine and 1 on the next. An
exported dashboard carries what each uid is (`sources`: `"25": { "kind":
"container", "name": "wslc-published" }`), and loading it changes each uid
for this agent's own resource of the same kind and name. An object whose
resource this agent does not have is loaded without a source, for the user to
choose; Tools names those resources before anything is loaded.

**Nothing is taken off.** An object whose resource is not on this agent (a
file from an agent that carried no names, or a container deleted since) stays
in the dashboard: in design it says its container (or image, volume,
network) is not on this machine, for another to be chosen; out of design it
is not drawn, since there is nothing it could show. Choosing another source
brings it back.

**JSON against JSON.** Tools compares each of the four views of the user's
dashboard with the release's (the installation's, or the package folder's
when newer) and with the agent's own default, object by object and card by
card: added, removed, and changed with each property before and after
(`size: small → large`). The user's own edits show as differences too; that
is the point. **Load the release's** puts the release's view in, **Reset to
the default** the agent's own (else the release's), **Back to the release**
every view at once.

**Save as default works on any agent.** On a development agent it writes
the repository, as before. On an installed one it makes the view the
agent's own default (`loaded-defaults/dashboard-default.own.json`), view by
view over the shipped one, which it never touches: what a user with none is
given, what a blank view shows and what Reset puts back. Each own view
records the shipped version it was based on, so a newer shipped or waiting
view is offered there too. Tools has, for any of the four views: **Load**
views into your dashboard (a file, or the newest default), **Reset** your
views to the default the agent has, **Load from file** into the agent's
default, and **Back to release** for the agent's own default views.

A development agent keeps reading its repository's objects' defaults, so
what is designed on it is what it shows and ships; it refuses to load a file.
An installed agent keeps what is designed on it apart, as the user's
changes (`loaded-defaults/object-defaults.changes.json`): only the kinds
changed, each with the base's value it replaced, over the base — the
shipped defaults or an official file loaded. A newer base, from an update or
a file, brings its new and improved kinds and leaves the user's as they are;
a kind changed by both is a conflict, which Tools lists to settle kind by
kind (keep mine, take new). **Shipped base** puts the shipped defaults back
under the changes; **Drop my changes** removes them. Whatever is replaced or
removed is backed up first (`loaded-defaults/backups/`, all kept for now). Export
carries the base with the changes over it.

Three pieces, one file format:

1. The two shipped files carry a version and the agent version they need.
2. New versions of them reach a machine between installers, and the user
   chooses whether to load them.
3. A user exports their own dashboard, all of it or some views, and loads it
   on another agent.

## Today

| File | Where an installed agent reads it | Can the user change it there? |
|---|---|---|
| Object defaults (`object-defaults.json`) | embedded in `wslc-ai-agent.dll` | no |
| Default dashboard (`dashboard-v2.5.default.json`) | embedded; copied once to `%LOCALAPPDATA%\WSLC-AI-Agent\data\dashboard-v2.5.json` the first time the dashboard is asked for | yes, that copy is the user's |

A copy of the user's dashboard is never replaced by a newer default: an
installer updates what the agent ships, not what the user keeps. Uninstalling
leaves `data\` in place, so a reinstalled agent opens the dashboard it had.

## 1. Version and minimum agent version

Both shipped files, and every exported dashboard, carry two fields at the top,
around the content (the format above):

```json
{
  "version": 5,
  "minAgentVersion": "1.0.12",
  "content": { "...": "the content as today" }
}
```

- `version`: the file's own version, an integer that only grows. It is what
  says a file is newer than the one in use.
- `minAgentVersion`: the oldest agent that understands everything in the
  file. It goes up when the file uses an object type, a part or a property
  that an older agent does not know.

An agent never loads a file whose `minAgentVersion` is above its own version.
It says why instead: *"Object defaults v5 need agent 1.0.12 or later; this
agent is 1.0.11. Update the agent."*

## 2. Shipped files and their updates

### Every installer carries the latest

Each release's installer embeds the latest version of both files and installs
them as that version's shipped defaults, always. The two files are also
attached to the release beside the installers.

### Between installers

A changed default does not need a new installer: the file is published on its
own with its `version` raised (see [Open points](#open-points) for where).

### The update line

The line that installs and updates the agent
(`irm https://berpiztu.github.io/wslc-ai-agent/install.ps1 | iex`) handles the
two files beside the installers:

- Installers: as today, downloaded and installed when the release is newer.
- Each JSON file: its published `version` is compared with the one in the
  package folder; only a newer one is downloaded into the package folder.
  Nothing is loaded by the line itself.

When a downloaded file needs a newer agent than the one installed, the line
says so; the installer it downloads in the same run usually is that agent.

### Loading in Tools

Tools shows, for each of the two files, the version in use and the one in
the package folder:

| Case | Shown |
|---|---|
| Newer file, agent new enough | *"Object defaults v5 available (in use: v4)"* and **Load** |
| Newer file, agent too old | *"Object defaults v5 need agent 1.0.12 or later (this agent: 1.0.11)"*, **Load** disabled |
| Nothing newer | the version in use, nothing to do |

Loading is the user's choice; nothing changes on its own.

- **Load object defaults**: the file is copied to
  `data\object-defaults.json`, which the agent reads instead of the embedded
  one from then on. **Reset to shipped** removes it.
- **Load dashboard**: the user picks which views of the file to load (see
  below); the dashboard in use is backed up first.

A loaded file keeps winning over the embedded one after an update, as the
user's dashboard does; Tools shows when the installed agent ships a newer
version, and **Load** or **Reset to shipped** takes it.

## 3. Export and import a dashboard

For moving a dashboard designed on one agent (a production server, say) to
another installation.

### Export

Tools → **Export**: the user ticks any of the four views,

- System landscape
- System portrait
- User landscape
- User portrait

and downloads one JSON file with those views, in the format above, plus:

- `minAgentVersion`: the exporting agent's version, the safe floor, since that
  agent knows every object in it;
- where it came from and when (the agent's name and the date), to recognise it
  later.

Objects that read one of the machine's own resources (a container, an image,
a volume, a network, a container's chart) are exported as they are; on
another machine they show nothing until their source is chosen again.

### Import

Tools → **Load from file**, from a file:

1. The file is checked: a dashboard file, and `minAgentVersion` not above this
   agent's version. Otherwise nothing is loaded and the reason is shown.
2. The views the file holds are listed; the user ticks which to load.
3. The dashboard in use is backed up, then only the ticked views are
   replaced; the rest stay as they were.

The shipped default dashboard and an exported one are the same format, so the
same **Load** reads both.

## Open points

- **Repositories and where the files are published.** Releases carry the
  installers; a file published between releases needs a place the update
  line reads without a release, for example a `defaults/` folder served by
  GitHub Pages, as `install.ps1` is. To be decided with the repositories.
- **Backups of the dashboard**: how many are kept, and whether Tools can
  restore one.
- **The User page and the server shown**: the User page depends on which
  agent the client is connected to, or which server it shows. To analyse once
  Tools is tested (the owner, 1 October 2026).
