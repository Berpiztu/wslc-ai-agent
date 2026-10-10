# WSLC AI Agent vs Docker Desktop

Part 1 is the analysis: what each product does, feature by feature, with
sources. Part 2 is the article for X, built only on what Part 1 verifies.

Facts as of 9 October 2026.

---

# Part 1 — Analysis

## What only WSLC AI Agent has

None of this is in Docker Desktop, and the first seven are not in plain `wslc`
either.

| # | Feature | WSLC AI Agent | Docker Desktop |
|---|---|---|---|
| 1 | **An MCP server that manages the machine** | Built in at `/api/v1/mcp`, with **54 tools**. Claude, Hermes, OpenClaw or any MCP client drives it from wherever it runs, with an API token. A skill installs with one click, also over SSH. Destructive tools are **hidden** until switched on, then each one asks for approval | Gordon, Docker's own agent, inside the app on the machine. The MCP Toolkit runs *other* MCP servers |
| 2 | **A dashboard you design, with a full designer** | Drag and resize live objects on a grid: readings, dials, charts, logs, events, and cards for containers, images, volumes and networks. Group them into cards (Ctrl+G), undo (Ctrl+Z), and use marquee selection. A properties window sets size, type, colours, elevation, alignment, margins and the style of **every part** of an object. There are two pages, each with a landscape and a portrait layout, and five ways to fit any screen. Alarms turn an object red. It is kept on the server for every client, and drafts survive a power cut | A fixed screen |
| 3 | **Notifications and alerts you configure: CPU and memory of the host and of every container** | Watched by the agent itself, with no screen open, and set in Settings › Notifications, the same for every client (or by your AI agent, over MCP). **Readings**, each on or off with its percentage and the minutes it must last (0: at once): host disk, host memory, host CPU, and the memory and CPU of **each container**. **Events**: a container that stopped on its own, one its restart policy could not start, a failed health check (wslc 3.0.2 on), the session gone down, a pull, build, transfer or backup failed or finished, the agent's update announced (with a **Cancel** button on the notification), installed or failed, and a reading back under its threshold or a container healthy again. **Delivered** as Windows notifications (tray and Windows app) and as **Android push** through Firebase, so the phone gets them without being connected to the agent; two Android channels, Alerts and Info, to silence apart; one phone for several agents, each push saying which; a tap opens the page; the last 200 kept, and a client that was away catches up. **Alarms on the dashboard** besides: a threshold on every measure, past it the object turns red and blinks, and chosen alarms show as rings in a status bar on every screen | —
| 4 | **Publish a container on the internet through a reverse proxy, with a login** | One switch in the container's form publishes a port on a public HTTPS name, through the wslc-published proxy container (nginx plus a login). **Every name has a login**: an app with its own login is let through, and any other gets the proxy's, with users per name | — |
| 5 | **A remote console that works from a phone** | The host's terminal and a shell in any container, in the browser and in the apps, from any network. A **key row** brings what a phone keyboard lacks: Esc, Tab, **Ctrl**, **Alt**, Shift, arrows, Home, End, PgUp, PgDn. Ctrl and Alt hold for the next key and Shift locks, so `nano`, `vim`, `htop` and Ctrl+C all work from the phone. The session survives leaving the page | A container shell in the GUI, on the machine itself only |
| 6 | **Self-update from GitHub or from your own folder** | The agent and every client update from the latest **GitHub release** (one line: `irm …/install.ps1 \| iex`) or from a **package folder you choose**, a share where you put your own builds. A one-minute countdown shows on every client and device, and any of them can cancel. It waits for transfers and backups, and the previous agent comes back if the installer fails | Updates only from Docker's servers |
| 7 | **Restart policies on WSLC** | `wslc` has no `--restart` (`wslc container run --help` lists none). The agent enforces `no`, `unless-stopped` and `always` itself: when it starts and when a session starts | Docker Engine has them; plain `wslc` does not |
| 8 | **The full UI from any network** | Browser (also as an installable PWA), Windows app and **Android app**. A user and password, or an API token. Reverse SSH tunnels to a VPS, so no port is opened at home | — (local desktop app) |
| 9 | **Open a container's web page that is not published** | A browser runs on the agent's machine and streams to you, with mouse, keyboard, touch and clipboard, plus saved logins | — |
| 10 | **Edit a container that exists** | Change ports, env, volumes or anything else and save. It is recreated, rehearsed first under another name, and the old one is restored if it fails | — (remove it and run it again) |
| 11 | **Update an image in place** | Pull a new version and recreate the container with everything it had. The old one stays if the new one does not start | — |
| 12 | **Paste a `docker run` line, checked before it runs** | It fills the form and flags names in use, ports taken, missing networks, and a command that listens on another port than the one published | — |
| 13 | **Network map** | Networks as hubs, containers as nodes with their addresses, filtered by network or container | — |
| 14 | **VHD volumes and disk compaction** | Volumes as virtual disks with a size, fixed or growing. A session's VHDX compacted, with its size before and after | — |
| 15 | **Files inside images and volumes**, and transfers owned by the agent | Browse, edit, upload and download. Transfers keep running when the view closes and every client sees them | Files of a container |
| 16 | **History of every CLI command** | Every `wslc` the agent ran, with its time, exit code, command line and output | — |
| 17 | **One client, several machines; several clients, one machine** | Clients share jobs (runs, pulls, transfers, updates) and any of them can follow or cancel | — |
| 18 | **$0 at any company size** | MIT | $24 per user per month (Business) above 250 employees or $10M revenue |

## What is being compared

| | **WSLC AI Agent** | **Docker Desktop** |
|---|---|---|
| What it is | A Windows agent beside `wslc`, Microsoft's container CLI built into WSL: web UI, Windows and Android apps, tray icon, REST API and MCP server | Docker's desktop application: Docker Engine in a VM, a GUI, CLI tools and Docker's add-on services |
| Container engine | WSLC (`wslc.exe`), part of WSL since 29 Sep 2026, WSL 2.9.3 or later | Docker Engine, on a WSL 2, Hyper-V or Docker VMM backend |
| Platforms | Windows (agent); browser, Windows and Android (clients) | Windows, macOS, Linux |
| Licence | MIT, free for everyone, companies included | Free (Personal) only below 250 employees **and** $10M revenue; otherwise $9–$24 per user per month |
| Source | Open source | Proprietary application (the Engine underneath is open source) |

WSLC is Microsoft's answer to "I need containers on Windows": a native
engine inside WSL, with no Docker Desktop. What it does not have is a
graphical interface. Microsoft ships none and points to community projects
instead. WSLC AI Agent is that missing layer, and it is built for AI agents as
much as for people.

## Cost

| | WSLC + WSLC AI Agent | Docker Desktop |
|---|---|---|
| Individual, small company | $0 | $0 (Personal) |
| Company over 250 employees or $10M revenue | **$0** | Pro $9/$11, Team $15/$16, **Business $24 per user per month** (annual/monthly billing) |
| 100 developers on Business, one year | **$0** | **$28,800** (100 × $24 × 12) |

Docker's paid plans also bundle Docker Hub, Build Cloud, Scout and
Testcontainers Cloud. Some companies want those; others pay only for the
desktop.

## AI and MCP

| | WSLC AI Agent | Docker Desktop |
|---|---|---|
| Built-in MCP server that manages the containers | **Yes**: `/api/v1/mcp`, **54 tools** (containers, images, volumes, networks, sessions, publishing, notifications, logs, system) | No built-in one. The MCP Toolkit runs *other* MCP servers (a catalog of 300+) for your agent. Docker-management MCP servers exist in that catalog and in the community |
| Bring your own AI agent | **Yes**: Claude, Hermes, OpenClaw or any MCP client | Gordon is Docker's own agent. MCP Toolkit servers connect to Claude, Cursor and others |
| Agent reaches the machine remotely | **Yes**: over MCP with an API token, from another machine or across the internet | Gordon runs on the machine (Desktop tab or `docker ai`) |
| A skill that teaches the agent the product | **Yes**: served by the agent and installed with one click into Claude Code, Hermes Agent or OpenClaw, also on another machine over SSH | — |
| Safety for destructive actions | Remove, prune, kill, exec, publish and stop-session are **not offered at all** until switched on. Once on, each one asks for approval | Gordon shows every action for approval; permissions last for the session |
| Paste a `docker run` line, checked before it runs | **Yes**: for people and for agents (names in use, ports taken, missing networks, a wrong listening port) | — |
| Local LLMs | — | Docker Model Runner |

**Reading:** Docker built an AI agent *into* its desktop app. WSLC AI Agent
made the machine itself an MCP server that any agent drives, from wherever
that agent runs, with the destructive part off until you allow it.

## Managing containers

| | WSLC AI Agent | Docker Desktop |
|---|---|---|
| Lists of containers, images, volumes, networks | Yes, as tables or cards | Yes |
| Run, start, stop, restart, kill, remove | Yes | Yes |
| **Edit an existing container**: change its settings and recreate it, rehearsed under another name, old one restored if it fails | **Yes** | — (remove it and run it again, or use Compose) |
| **Update an image in place**: pull a new version and recreate the container with everything it had, keeping the old one if the new one fails | **Yes** | — |
| Build, tag, push, save, load, import | Yes | Yes (Build Cloud is extra) |
| Publish a version (`registry/owner/app:1.4` plus `latest`, pushed) | Yes, one step | Through the CLI |
| Logs, live stats, shell, files inside a container | Yes, plus files inside **images and volumes** | Yes |
| File transfers that go on after the view closes | Yes, owned by the agent and seen by every client | — |
| Network map (networks as hubs, containers as nodes with addresses) | **Yes** | — |
| VHD volumes with size; session VHDX compaction | **Yes** | — |
| Restart policies | Kept by the agent (`wslc` has none) | Kept by Docker Engine |
| **Compose** (multi-container stacks) | **No**: `wslc` has no Compose yet; Microsoft has announced `compose up` as planned | **Yes** |
| Kubernetes | No | Yes |
| Extensions marketplace | No | Yes |
| Image security scanning | No | Docker Scout |

## Remote access and mobile

| | WSLC AI Agent | Docker Desktop |
|---|---|---|
| UI from another PC or a phone | **Yes**: the same UI in the browser (installable as a PWA), the Windows app and the **Android app** | — (a local desktop app; phone management is left to third-party apps) |
| Internet access with no port opened at home | **Yes**: reverse SSH tunnel to a VPS, with a guide | — |
| Login | Trusted on its own machine; username and password, or an API token, from anywhere else | Docker account |
| Host terminal and container shells in the browser, with a key row for phones (Esc, Tab, Ctrl, Alt, arrows…) | **Yes** | Container shell in the GUI, on the machine |
| **Open a container's web page that is not published**, from anywhere | **Yes**: a browser on the agent's machine, streamed to you (mouse, keyboard, touch, clipboard) | — |
| **Publish a container on a public HTTPS name** | **Yes**: one switch in its form, through an nginx proxy container, with a **login on every name** | — |
| Push notifications (thresholds, crashed containers, failed health checks, finished jobs) | **Yes**: Windows and Android, one phone for several agents | — |
| One client, several machines | **Yes** | — |

## Monitoring

| | WSLC AI Agent | Docker Desktop |
|---|---|---|
| Live CPU, memory, disk and network per container | Yes, with charts | Yes |
| **Dashboard you design** (drag and resize, landscape and portrait, kept on the server for every client) | **Yes** | — |
| **Alarms** with thresholds, a status bar on every screen | **Yes** | — |
| Recent `wslc` events | Yes (dashboard object, REST, MCP) | `docker events` (CLI) |
| History of every CLI command run, with output | **Yes** | — |

## Installation and running

| | WSLC AI Agent | Docker Desktop |
|---|---|---|
| Install without admin rights | Yes (per user) | Yes for a per-user install; an all-users install needs admin |
| What is installed | WSLC comes with WSL; the agent is one small program | The Docker Desktop application, with its own WSL distribution on the WSL 2 backend |
| Runs with every window closed | Yes: jobs, restart policies, alarms and notifications go on | Yes |
| Self-update | Agent and clients, from GitHub releases or a folder of your own, with a countdown anyone can cancel | From Docker |
| Enterprise control | Intune policies and Defender for Endpoint, on WSLC itself | Admin Console, Settings Management (Business) |

## Where Docker Desktop is ahead

Said plainly, because the article must hold up:

1. **Compose.** Multi-container stacks are everyday work, and `wslc` cannot
   run a `compose.yaml` yet. It is the biggest gap.
2. **Ecosystem**: Kubernetes, Extensions, Scout, Build Cloud, Testcontainers,
   Model Runner, Hub integration.
3. **macOS and Linux.** WSLC is Windows only.
4. **Maturity.** Docker Desktop has had years in production. WSLC has been GA
   since 29 September 2026.

## Where WSLC AI Agent is ahead

The 18 points at the top of this part: [What only WSLC AI Agent has](#what-only-wslc-ai-agent-has).

## Sources

- WSLC GA: [Windows Developer Blog, 29 Sep 2026](https://blogs.windows.com/windowsdeveloper/2026/09/29/wsl-containers-now-generally-available/),
  [4sysops](https://4sysops.com/archives/wsl-containers-run-linux-containers-on-windows-with-wslc/),
  [Virtualization Howto](https://www.virtualizationhowto.com/2026/10/microsoft-just-gave-wsl-its-own-container-engine-do-you-still-need-docker-desktop/)
- Docker pricing and licence terms: [docker.com/pricing](https://www.docker.com/pricing/)
- Gordon: [Meet Gordon](https://www.docker.com/blog/meet-gordon-dockers-ai-agent-for-your-entire-container-workflow/),
  [Gordon update](https://www.docker.com/blog/gordon-dockers-ai-agent-just-got-an-update/)
- MCP Toolkit: [docs.docker.com](https://docs.docker.com/ai/mcp-catalog-and-toolkit/toolkit/)
- Docker Desktop on Windows: [docs.docker.com/desktop/setup/install/windows-install](https://docs.docker.com/desktop/setup/install/windows-install/)
- WSLC AI Agent: [features](../features.md), [README](../../README.md)

---

# Part 2 — The article for X

## Docker Desktop is optional on Windows now. Here is what replaces it, and what it does that Docker Desktop never did.

On 29 September 2026, Microsoft made **WSL containers** generally available.
`wslc.exe` ships inside WSL. You can build and run Linux containers on
Windows with **no Docker Desktop and no licence**.

There is one catch: `wslc` is a command line. There is no GUI, no dashboard,
no phone app and no AI integration.

So we built them. **WSLC AI Agent**: open source, MIT, $0.

### Seven things Docker Desktop cannot do

**1. Your AI agent runs your containers.**
Claude, Hermes, OpenClaw or any MCP client, through the MCP server built into
the agent and a skill that teaches it WSLC. More on this below.

**2. A dashboard you design.**
It is not a fixed screen but a designer. You drag and resize live objects:
dials, charts, logs, events, and container, image, volume and network cards.
You group them (Ctrl+G), undo (Ctrl+Z), and style every part of every object.
There are landscape and portrait layouts, alarms that turn an object red, and
drafts that survive a power cut. It is kept on the server, the same on every
device.

**3. Notifications and alerts you configure.**
The agent watches on its own, with no screen open. You choose what it tells
you, the same for every client, or your AI agent sets it for you:

- **Readings**: host disk, host memory, host CPU, and the memory and CPU of
  **each container**. Each one is on or off, with its percentage and how many
  minutes it must last before it notifies.
- **Events**: a container that stopped on its own, one its restart policy
  could not bring back, a failed health check, the session gone down, a pull,
  build, transfer or backup that failed or finished, and the agent's own
  updates. You are also told when a reading falls back under its threshold.
- **Delivery**: Windows notifications on the PC, and **push on Android**, which
  reaches the phone even when it is not connected to the agent. Alerts and
  Info come as two channels you can silence separately. One phone serves
  several agents, a tap opens the right page, and the update announcement
  carries a **Cancel** button.
- **Alarms on the dashboard** too: past its threshold an object turns red and
  blinks, and the alarms you choose show as rings in a status bar on every
  screen.

**4. Put a container on the internet, behind a login, with one switch.**
A port goes to a public HTTPS name through a reverse proxy (nginx plus a
login, in one container). Every name gets a login, with users per name, and
an app that has its own login is let through. You write no nginx config.

**5. A real console from your phone.**
The host's terminal or a shell in any container, from any network. A key row
brings what a phone keyboard lacks: **Esc, Tab, Ctrl, Alt**, Shift, arrows,
Home, End, PgUp and PgDn. So `nano`, `vim`, `htop` and Ctrl+C work from the
phone, and the session survives leaving the page.

**6. Updates from GitHub, or from your own folder.**
The agent and every client update from the latest GitHub release, or from a
package folder you choose for your own builds. A one-minute countdown shows on
every device, and any of them can cancel. It waits for running transfers and
rolls back if the installer fails.

**7. Restart policies on WSLC.**
`wslc` has no `--restart`. The agent enforces `unless-stopped` and `always`
itself, when it starts at logon and when a session starts. Your services come
back after a reboot, as they would on Docker.

### And then there is the rest

- **Your AI agent, in detail: an MCP server with 54 tools**, built in. Claude, Hermes, OpenClaw or any
  MCP client drives your containers from wherever it runs, with an API token.
  Ask it to "Run this as is: docker run -d --name web -p 8080:80
  nginx:latest", or to "Publish port 80 of the web container on
  web.example.com". A skill teaches the agent WSLC, installed with one click.
- **Safe by default**: remove, prune, kill, exec, publish and stop are hidden
  from the AI until you switch them on. Then every one asks for your yes.
- **The full UI from any network**: browser, Windows app, Android app, and a
  reverse SSH tunnel to a VPS with no port opened at home.
- **Open a container's page that is not even published**: a browser runs on
  the agent's machine and streams to your screen.
- **Edit an existing container**: change it and save. It is rehearsed under
  another name, and rolled back if anything fails.
- **Update an image** and keep every setting of the container.
- **Paste any `docker run` line**: it is checked before it runs (names in use,
  ports taken, missing networks).
- A **network map**, **VHD volumes**, **disk compaction**, files inside images
  and volumes, transfers that keep running, and the history of every command.

### The numbers

- **$0** at any company size. Docker Business is **$24 per user per month**
  above 250 employees or $10M revenue. For 100 developers that is
  **$28,800 a year**.
- **54** MCP tools. **1** port for everything. **0** admin rights.

### Being honest

Need **Compose** today? Stay on Docker Desktop: `wslc` cannot run a
`compose.yaml` yet (Microsoft has it planned). Need Kubernetes, macOS, Scout
or Extensions? Docker has them.

If you run containers on Windows and want them **free, native, on your phone,
published in one switch and driven by your AI agent**, Docker Desktop is no
longer the default.

**WSLC AI Agent.** Open source. MIT. Windows + Android.
github.com/Berpiztu/wslc-ai-agent

---
