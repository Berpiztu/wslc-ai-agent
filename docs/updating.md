# Updating

The agent updates itself, and hands its clients their updates, from one
folder: the **package folder**. Put a newer installer there and the rest
follows.

| File in the package folder | What it updates |
|---|---|
| `wslc-ai-agent.msi` | The agent itself |
| `wslc-ai-client.msi` | The Windows client |
| `wslc-ai-client.apk` | The Android client |

## Where the package folder is

The agent's installer asks for it, beside the address and the port, and
creates it if it is missing. What it offers depends on where the installer
came from:

| Installer | Package folder it offers |
|---|---|
| From a [release](https://github.com/berpiztu/wslc-ai-agent/releases/latest) | `C:\Berpiztu\wslc-ai-agent` |
| Built yourself with `build-agent-installer.ps1` | The `dist` folder of your clone, where the build scripts put every installer |

The answer is written beside the agent, in plain text, in
`%LOCALAPPDATA%\WSLC-AI-Agent\wslc-ai-agent.ini`; nothing goes to the registry.

To see or change it later: **Settings → Update → Package folder**. An empty
field means the folder the agent was installed with, shown in grey inside it;
type or pick another and **Save folder**, or empty the field and save to go
back. A choice made
there wins over the installer's, and is kept across updates.

## Auto update

**Settings → Update → Auto update**, on by default.

- **On**: when a newer `wslc-ai-agent.msi` appears in the package folder and
  nothing has written it for 30 seconds, the agent announces the update to
  every client a minute before it starts — and to every device that gets its
  notifications, with **Cancel update** on the notification itself. Any of
  them can cancel it. It also waits for file transfers and backups to finish,
  since the update would cut them. Then it installs the new version and
  starts again, keeping its address, port and package folder.
- **Off**: nothing happens on its own. **Update now**, in the same tab,
  installs the newer installer when you choose.

Only an installed agent updates itself: one run from source
(`start-agent.ps1`) is never replaced by an installer.

The clients check the agent's package folder when they start, on every page
change and whenever the link to the agent comes back. When the installer
there is newer than the running client, they offer to install it. The System
card also offers each client for download from there.

## If you installed a release

1. Download the new installers from the
   [latest release](https://github.com/berpiztu/wslc-ai-agent/releases/latest).
2. Copy them into the package folder (`C:\Berpiztu\wslc-ai-agent` unless you
   chose another).
3. With Auto update on, the agent updates itself within a couple of minutes,
   and the clients offer their own update. With it off, press **Update now**
   in Settings → Update.

## If you build it yourself

The build scripts write every installer to your clone's `dist` folder, and an
agent installed from an installer you built looks for its updates there. So
building a newer agent installer **is** deploying it: with Auto update on,
the installed agent picks it up and updates itself.

- `build-agent-installer.ps1` raises the version each time, which is what
  makes the installer newer. It writes it to `private\version.props`, never
  tracked, so building changes nothing in git. `-NoBump` builds the same
  version again, which the agent does not take as an update.
- **To keep your installed agent from updating itself while you build**, turn
  **Auto update** off in Settings → Update. It then updates only when you
  press **Update now**. Or point its package folder somewhere else.
- `build-agent-installer.ps1 -Release` builds an installer that offers
  `C:\Berpiztu\wslc-ai-agent`, as the published releases do.

## Updating another machine

Copy the built installers into that machine's package folder, in whatever
way suits you (a shared folder, `scp`, a USB stick), and its agent updates
itself, and offers its clients their updates, from what arrives.

## Updating by hand

Running a newer `wslc-ai-agent.msi` yourself works too: it replaces the
installed agent. Its wizard asks for the package folder again, offering its
own default; a folder chosen in Settings still wins over it.
