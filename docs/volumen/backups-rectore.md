# Volume backups and restores

## What a volume backup has

- **Backup now.** A backup made at once, started by pressing a button.
- **Scheduled backups.** Backups made on a schedule. They come in a second
  version, but the first one has to leave room for them.

## Where backups go

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
`…\Volume backups\hermes-data\`, made when it is not there. A volume with a
folder of its own backs up into that folder itself.

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
