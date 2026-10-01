# Dashboard defaults, their updates and exporting a dashboard

Status: **specification, not built yet** (agreed 1 October 2026). Where the
files are published is still open: see [Open points](#open-points).

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

Both shipped files, and every exported dashboard, carry two fields at the top:

```json
{
  "version": 5,
  "minAgentVersion": "1.0.12",
  "...": "the content as today"
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

### Loading in Settings

Settings shows, for each of the two files, the version in use and the one in
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
user's dashboard does; Settings shows when the installed agent ships a newer
version, and **Load** or **Reset to shipped** takes it.

## 3. Export and import a dashboard

For moving a dashboard designed on one agent (a production server, say) to
another installation.

### Export

Settings → **Export dashboard**: the user ticks any of the four views,

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

Settings → **Load dashboard**, from a file:

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
- **Backups of the dashboard**: how many are kept, and whether Settings can
  restore one.
