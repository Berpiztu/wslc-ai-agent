# Volume and container backups and restores

Volumes and containers back up and restore the same way: one design, one
set of components, the same window. What a container adds is said in its own
section.

## What a volume backup has

- **Backup now.** A backup made at once, started by pressing a button.
- **Scheduled backups.** Backups made on a schedule. They come in a second
  version, but the first one has to leave room for them.

## Where backups go

Settings → Defaults starts with the **base folder**: each kind's backup
folder is a folder of it (`Projects`, `Containers`, `Volumes`) while its own field is left empty, its grey saying which, so
changing the base moves every one that follows it at once; a column given a
folder of its own keeps it, and emptied follows the base again. Then the
backups' defaults in three columns, **Projects**,
**Containers** and **Volumes** (one under the other on a narrow screen), each
in a box with its title and what it is for, then its folder, its **Stop
running containers** and, for projects and containers, its **What to back
up**.

The folder is never asked for at each backup. It comes from three levels,
each over the one below:

1. **The agent's Settings, Defaults tab.** The agent's default values live
   there; the backup folder is the first of them.
2. **The window of every volume's backups** (the Volumes page's verbs). It
   has a folder of its own, which starts as the Settings default. Changed
   there, every volume with no folder of its own backs up to it from then on;
   a volume with its own keeps it, and Settings stays as it is.
3. **Each volume.** Its backup screen shows its folder at the top: empty, it
   says **default**, the folder of every volume; with a value, that value.
   Changed there, that volume's backups always go to that folder.

A volume with no folder of its own (it says **default**) backs up to a folder
named after it inside the folder of every volume (the window of every
volume's backups, or, while that has none, the Settings default):
`…\Volumes\hermes-data\`, made when it is not there. A volume with a
folder of its own backs up into that folder itself.

## How backups are made

Two settings go the same three levels as the folder, each beside it: in
Settings → Defaults, in the window of every one's backups, and in each one's.
At the window's two levels each starts at **Default**, which takes the level
below and says what that gives now (`Default (Ask)`).

- **Stop running containers**: what a backup does when a container it needs
  stopped runs (see Running containers).
  - **Ask**: it asks, naming them, whether to stop them; yes, they are
    stopped and started again when it ends; no, nothing starts.
  - **Yes**: they are stopped without asking and started again.
  - **No**: the backup is refused while they run, saying which.
- **What to back up**, a container's and a project's: a small tree in a box
  of its own, **All** and under it, a step in, **Configuration**, **Image**
  and **Volumes**. All ticked ticks every one, unticked unticks them, and
  each is then ticked alone; All shows a dash while only some are. With none
  ticked it says so in red, and neither Save nor Back up can be pressed until
  one is. In a container's own settings the tree is that container's: its
  image by name beside Image, and under Volumes each of its volumes, all
  ticked by default; a volume unticked is left out of its backups (its
  settings keep what is left out, so a volume it is given later is backed
  up), and the containers that use only that volume are not stopped for it.
  With **Default**
  ticked, the level below's ticks show and cannot be changed; unticked, the
  level's own start from those. The agent's default ticks all of them until
  it is changed. At each backup the same ticks are asked, as its settings
  tick them, to change for that backup alone.

## Containers

A container's backup is the same as a volume's, in the Containers page and
in each container's menu (**Backup & restore**), with one choice more,
asked when it is made, ticked to begin with as its settings say (How backups
are made), any of its parts:

- **Configuration**: its settings, to make it again as it was.
- **Image**: its image.
- **Volumes**: its volumes' contents.

A container's backup is a folder named by its date, `2026-10-28_065115`, in its backup folder
(the same three levels, `Containers`): `backup.json` says what it
holds (and, with its configuration, its settings), `image.tar` the image, `volumes\` one
`.tar.gz` per volume, packed as a volume's backup is. A folder of the machine
mounted into it is not a volume: it is not in the backup, and the log says
so. A restore puts back what the backup holds: the image loaded, each
volume's contents replaced (created when it is not there), and with its
configuration the container made again, stopped, with its settings; one that is not there is
created, a new machine's. Its Backups table has a What column.

**Its networks go with its configuration.** A backup that holds a
container's configuration keeps, beside it, each network it is attached to
as it was made (`networks.json`: driver, subnet, gateway, options; a
project's backup once, in its date's folder, for all of its containers);
WSLC's own (`bridge`, `host`, `none`) are always there and are not kept. Put
back, a network that is missing is made again before the container; one that
is there is left as it is, and the log says so when its subnet is not the
backup's. The tree shows each network under the configuration.

Its Backups tab is a tree, as a project's is: a backup by its date, and
under it, a step in, **Container configuration** (its settings), **its
image** and **each volume**, those it holds. Each row restores what it is,
alone: the date all of it, the configuration making the container again,
the image loaded, a volume put back as its own backup is. A project's tree
shows each container's image and volumes the same way, under it.

The old container backup (an export downloaded by the browser) is gone.

## Running containers

- **A backup is made with the containers stopped.** A volume's backup
  needs every container that uses it stopped; a container's with its
  volumes needs every container that uses them stopped, it among them. Those
  running are stopped, asked first or not, or the backup is refused, as its
  **Stop running containers** says, and the agent starts again the ones it
  stopped when the backup ends, well or not: each once what it depends on
  runs. One that does not start again is said in the backup's log, and the
  backup, which is written, ends in error saying so. A container's
  configuration and image are backed up while it runs.
- **A restore stops nothing.** It is refused while a container that uses
  what it puts back runs, saying which to stop.
- **A container does not start while a volume it uses is being backed up or
  restored**: not its Start, Restart or Run, nor its restart policy. It is
  refused, saying which volume and why, until that ends.

### Why stopped, and not a shadow copy

Windows has the Volume Shadow Copy Service (VSS): it freezes a disk at one
instant, copy-on-write, so a copy reads the disk as it was then however much
is written after, and the programs that take part in it (SQL Server,
Exchange: VSS writers) put their data in order just before that instant. It
does not copy first and add the changes after; what is written after the
instant is not in the copy. It does not serve the agent's volumes as it is:

- **A `guest` volume** (the default) is not a Windows file. Every one of them
  lives inside the WSL machine's own virtual disk, in ext4. VSS would only see
  that whole disk, while the machine writes to it: no volume alone, and what
  it took would be as after a power cut in the middle of a write.
- **A `vhd` volume** is a VHDX of its own on the Windows disk, so VSS could
  copy that file with its containers running. The copy is crash-consistent,
  as after a power cut, not put in order: a database usually recovers from
  that, but nothing promises it.
- **Inside Linux** the same thing is a file system's snapshot (LVM, btrfs,
  ZFS). The WSL machine's disk is ext4 without LVM: there is none.
- **Freezing a container for a few seconds** (`pause`, as Docker has it) is
  not in `wslc container` (its help lists no such command; checked on
  2026-10-28).

What could be done without stopping, none of it built:

1. **Copy while it runs**, said so: the copy may be half of a change if the
   container writes meanwhile. Good enough for a volume of files that seldom
   change.
2. **The application's own dump** run inside the container (`pg_dump`,
   `mysqldump`): the right way for a running database, one per application.
3. **VSS for `vhd` volumes alone**, crash-consistent.

Any of them would be a fourth choice of **Stop running containers**
(*Copy while running*).

## The window

Built as a project's windows are, and to the UI rules (`docs/RULES.md`,
`docs/ui-controls.md`):

- **A volume's own Backup & restore** (its card, its row) shows that volume
  alone: its tabs, no list of volumes, as a container's own Update image is.
- **The Volumes page's Backup & restore** is built exactly as a project's
  window (`ProjectPanesDialog`): the same list, body and size, the list at the
  left with All first, the same tabs at the right.

- **At the left, the list.** **All** first, then each volume, with those
  only the history still names (removed since). A volume being backed up or
  restored turns a wheel in its slot.
- **At the right, MudBlazor's tabs** for what is chosen:
  - **Backups.** A volume: its backups, a table (`ListGrid`): When, Size (a
    number, at the right), File, the newest first, and the Actions column
    pinned at the right with Restore (amber) and Delete (red); a file no
    longer there says so before its path, in red. A volume restored from a
    backup lists that backup too. All: every volume's backups, with a Volume
    column; a backup or restore under way stands above them, its bar, log
    and cancel in its Volume cell.
  - **Logs.** A volume: the log of each of its backups and restores the
    agent keeps. All: every volume's, each line said by its volume.
  - **Settings.** A volume: its own backup folder; empty, it says
    **default**, the folder of every volume. All: the folder of every
    volume; empty, the Settings default. Scheduled backups come here later.
- **The title row holds the open tab's verbs and Close**, never in the
  content: Back up and Restore in Backups, Save in Settings. Close asks while
  a folder is not saved.
- **A project's window has a Backup tab**, after Zaindari: the container
  chosen's backups, logs and settings, as its own Backup & restore shows
  them, the same component (`BackupsView`); their verbs on the window's title
  line, before Close, which asks while a setting is not saved. With All
  chosen, the project's own (Projects, below).

## Projects

- **A project backs up whole**, from its window's Backup tab with All
  chosen, as its tree decides: All; under it the project's configuration
  (its Compose file) and Containers; under each container its
  configuration, its image by name and Volumes, each volume a step further.
  Each box shows what it gives: what the project decides, else what its
  container's own settings say (a container that does not back up its image
  shows it unticked). **A box the project touches is the project's to
  decide, and a parent touched decides every box under it**: ticking
  Containers backs up every container whole, whatever each one's settings
  say. A box never touched follows its container. Default, at the top of the
  project's settings, forgets what the project decided. At each backup the
  same tree is asked, ticked so, to change for that backup alone (only the
  volumes of every container today, say). A container's own backup, alone,
  follows its own settings.
- **On disk, the tree it is shown as.** Always in a folder named after the
  project, inside its own backup folder, else inside the folder of every
  project (`Projects` until another is chosen), so a folder several
  projects share keeps each one's dates apart; in it, a folder per backup by
  its date:

  ```
  Projects\
  └─ ia-apps\
     └─ 2026-10-28_065115\
        ├─ compose.yaml, project.json      the project's configuration
        ├─ networks.json                   the containers' networks, as made
        ├─ project-backup.json             what this backup holds
        ├─ containers\
        │  ├─ ia-voice\                     its settings (backup.json) and image.tar
        │  └─ ia-analysis-backend\
        └─ volumes\
           ├─ voice-data.tar.gz
           └─ shared-uploads.tar.gz        used by two containers: kept once
  ```

  Volumes are files, one each, beside the containers and not under them: a
  volume two containers use is kept once, and a container with four
  volumes is four files in one folder. A container's `backup.json` says
  which volumes it uses and where they are, so it is restored alone as well.
- **A container of a project whose own folder is Default** is backed up the
  same way, from its own window too: a date of its project's backups holding
  it alone. A project's containers' backups are all in one place, however
  they are made, and each is in its container's own history as well.
- **Every ring turns, as a project's stop does.** The project's card and row
  show its backup or restore, with its log and its cancel; each of its
  containers' backup or restore is a job of the container's own from the
  start, waiting its turn, then on its own steps, its ring on its card and
  its bar in the table, with its log and its cancel. A container's cancelled
  stops the project's; the project's cancelled stops them all, and one never
  reached says so.
- **Stopping** is once for all of them: the running containers that use
  their volumes, asked, stopped and started again as one.
- **Its Backups tab is a tree of two levels**, in a table as every list is,
  the line round each backup as round a project's rows on the Containers
  page. The parent is a backup, by its date and time (the window is the
  project's: its name is not said again). Under it, a step in and side by
  side, **Project configuration** (its Compose file) and **each container's
  backup**, and under each container, a step further, **its volumes**. Only
  a date has a +/−; the tree opens folded, the dates alone,
  and the column's head unfolds or folds every backup at once. No ticks.
- **Each row has its own verbs.** A date's: Restore all of it, Delete all of
  it (its folder and each container's backup). The configuration's: Restore
  the Compose file alone. A container's: Restore it alone, Delete it alone
  (with the volumes no other container of the backup uses), and the
  project's backup no longer holds it. A volume's: Restore that volume
  alone, as its own backup is put back. Each asks first, saying what
  it replaces.
- **A restore** puts back what its row holds: all of it, the Compose file
  alone, one container, or one volume; refused, before anything is put back,
  while one of them runs. A volume two containers use is put back once.
- **The ring of a backup or restore under way is on the volume's card**,
  never in the window.
- **Nothing in it changes size with what it shows.** A window takes its size
  when it opens, from the viewport, and changes it only when the viewport
  changes (a phone or a tablet turned, a browser window resized): never for
  something inside it, a value, a list, a step. A field is one line: a value
  longer than it scrolls inside it, never makes it two or three lines. The
  notes say the same words for every volume.

## Errors

- **A backup's log must stay whole.** A backup was started, and its log opened
  from the volume's card: it showed the backup at work, then all of it went
  and only a short summary was left. The whole log of what was done has to
  stay, to be read afterwards.
- **One verb on the Volumes page.** There were two verbs, one for backups and
  one for restores, for a single window: there is one, **Backup & restore**.
- **All lists every backup.** All's Backups showed only what was under way:
  it lists every volume's backups.
- **A failure says what failed, in words a person reads.** A backup of
  hermes-data ended with tar's own line about a socket: the step that failed
  and why are said, the command's prefix left out, and the log keeps all of
  it. A socket (a running program's, made again when it starts) is never
  part of a backup: it is left out, and said so in the log.
- **A restored volume lists the backup it came from**, as well as the volume
  that backup was made of.
- **Backups have their own place in the agent's log.** A volume's Logs
  showed only the backups since the agent last started. Every line of a
  backup or restore is written in the agent's log under a group of its own,
  **Backups**, on the Logs page with the others. A volume's Logs, and All's,
  read the same lines from a small file of their own (`volume-backups.log`),
  kept across restarts: reading the whole agent's log every second was
  slow.
- **A container's logs and a project's show their backups among their
  lines.** A container's Logs (its window, its row's Logs, a project's
  window with it chosen): its own backups and restores in the order they
  were written among what the container wrote, which stands as it wrote it;
  each backup line is an entry, `time | LEVEL | backup | message`. A
  project's Logs with All chosen: every line an entry, `time | LEVEL |
  whose | kind | message`, whose the container or the project, kind
  `output` or `backup`: the time is the one wslc stamped (the agent's for a
  backup), on this machine's clock, the same for every container however
  each writes its own; the parts take the Logs page's colours
  (`LogLines.Entry`). Two keys of the rail show either alone: what the
  containers wrote, and the backups. A volume's and an image's logs stay as
  they are.
