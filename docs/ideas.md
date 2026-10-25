# Ideas

Features worth building if a real need shows up, not planned work. Each idea
says why it could matter, what the agent already has, what is missing, and
what has to be decided or checked before it is designed. When one is picked
up, it moves to its own issue and leaves this page.

## Usage quotas: traffic and volume space

### Why

A hosting provider sells a server with a monthly traffic allowance that must
not be exceeded. Hosting containers for customers adds a second need: each
customer pays for a share of traffic, or for a volume of a given size, and has
to be watched against it. With a quota there is a 100 %, so "customer A has
used 85 % of their 500 GB this month" or "this volume is 90 % full" fits the
dashboard's existing alarm model (a percent with a threshold) exactly.

Without a quota, an alarm on traffic or disk throughput is not worth having:
high I/O is usually healthy (pulls, builds, databases, backups), and what
actually hurts is already watched by the CPU, memory and drive-space alarms
and by the container notifications.

### What exists

- The dashboard's Disk and Network charts and the Volumes and Networks pages
  show the bytes read, written, received and sent by containers: the
  `BlockIO` and `NetIO` counters of `container stats`, summed
  (`HomeService.IoAsync`, `VolumeSummary`).
- Alarms watch percents only: CPU, memory and the space used on the drive that
  holds the sessions' VHDX files (`DashboardMeasures`, `ObjectAlarm`).
- Notifications already warn on CPU and memory thresholds, and reach the tray
  and the Android client.
- A `vhd` volume is created with a size, so it has a real ceiling
  (`CreateVolumeRequest.Size`).

### What is missing

- **Traffic counted per billing period.** The `stats` counters start at zero
  every time a container starts and disappear when it is recreated. Billing
  needs the agent to add up each container's or project's traffic by its
  registry uid, which survives a recreation, keep it on disk, and start over
  at each period.
- **Two different meters.** The provider counts everything that crosses the
  server's network card: containers, Windows Update, RDP, backups. The
  server's allowance has to be read from the Windows network interface. A
  customer's share comes from their containers or project.
- **The space a volume uses.** `wslc` has no `system df` and the agent does
  not read volume sizes today. A `guest` volume has no ceiling, so its quota
  would be a number the user sets.
- **An alarm with a quota as its 100 %**, on the dashboard and as a
  notification at chosen thresholds (for example 80 % and 100 %). A quota is
  watched when nobody is looking at the screen.

### To decide or check first

- How to measure the used space of a `vhd` volume (the VHDX file on the host?)
  and of a `guest` one (`du` inside the utility VM?): try both on a machine
  before designing anything.
- The period: calendar month, or a billing day of the month.
- What a quota belongs to: a container, a Compose project, or a customer
  holding several projects.
- What happens at 100 %. A warning only, or an action such as throttling or
  stopping the container. An action is destructive, and goes through the
  approval gate like the other destructive tools.
- The API: a usage endpoint in `/api/v1`, a matching MCP tool, and
  `ApiCompatibility.Level` raised.
