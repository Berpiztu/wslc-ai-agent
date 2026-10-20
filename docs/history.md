# The history of the project

## A Python dashboard (June 2026)

WSLC AI Agent began on 2026-06-30 as a small Python project, `wslc-dashboard`:
a simpler Docker Desktop for everyday use, for the containers WSL had just
learned to run with `wslc`. FastAPI and HTMX served a web page; the first day
brought a containers table with live stats and actions, and an Images page
that showed a pull's progress as it went.

## Reaching `wslc` from somewhere else (July)

The dashboard ran in a Linux container of its own, so it had to reach `wslc`
on the Windows host. First through an HTTP proxy (2026-07-07), then over SSH,
set from the dashboard's Settings (2026-07-08). Sessions could be started
and stopped from the page, containers recreated, files browsed inside them
and a console opened in them; by 2026-07-14 it covered the whole `wslc`
command line of WSL 2.9.4.

Then the tests met the first real problem: authentication over SSH. What
OpenSSH starts is not the session of the user signed in at the machine, and
`wslc` keeps its sessions, images and containers per user, in that session.
Commands run over SSH were not the user's own: an image pull stopped at 0%,
or ended in `E_ACCESSDENIED`.

So we tried `runas`: SSH carried the connection, and `wslc` ran as the
signed-in user, in that user's session (mid-July). That brought the second
problem. The machine's owner signs in with a Microsoft Entra ID (Azure AD)
account, and OpenSSH would not take that principal. A local account took
over SSH, and `wslc` still ran as the Entra ID user through `runas`, trying
each form Windows might accept for the credentials, `DOMAIN\user`, the name
as given and the UPN `user@domain`, until one went through (end of July).

It worked, with two identities to keep in step and a bridge in the middle.

## An agent in the user's session (August)

On 2026-08-03 the design changed. More SSH was not the answer, and neither
was a Windows Service, which runs in session 0 and meets the same wall.
`wslc` had to run locally, as the user who owns the containers, in that
user's session. The dashboard became an agent with an API only, started by a
logon task of that user, with no administrator rights, and taking a token
from the clients on the network. SSH was retired for `wslc`, and that is
still what the agent is.

Ten days later, on 2026-08-13, .NET MAUI was chosen for native clients, and
the first Windows client connected to the agent's API.

## Rebuilt in .NET (September)

Through August and early September the Python agent grew: sign-in from the
internet, client packages served by the agent, a network map, and the plan
for an MCP server, so AI agents could use it too (2026-09-12).

When the time came to rebuild it all in .NET, the specification was written
first, from what the Python project had shown was needed:

- **One interface**, for the web, Windows and Android.
- **A dashboard** each user designs.
- **Alarms**, which became notifications.
- **Automatic updates**, of the agent and the clients.
- **MCP**, for AI agents.
- **Remote access**, from anywhere.
- **Files** and **transfers**.
- **Terminals**, the host's and every container's.
- **Publishing**.
- **Backups**.
- **Network map**.
- **Saved logins**.

On 2026-09-13 the rebuild began, in a new repository, `wslc-agent`: one
ASP.NET Core agent serving a Blazor interface and an MCP server, and one set
of Razor components for the web, the Windows app and the Android app. The
next day brought the installers, the per-user logon task and the Android
client; on 2026-09-18, the MCP skill for AI agents.

## Published (end of September)

On 2026-09-29 the project was published here as WSLC AI Agent, and
[1.0.0](https://github.com/Berpiztu/wslc-ai-agent/releases/tag/v1.0.0)
came out the next day: the agent, the web dashboard, the Windows and Android
clients and the MCP server. A release a day, or several, followed.

## The dashboard

The Home page is not a fixed screen: each user designs it, and it has had
work in every release since. An editor with:

- **Cards**, ready-made or built, and cards inside cards.
- **Objects**: readings, dials, charts, tables, events.
- **Pages**: System and User.
- **Views**: landscape and portrait.
- **Screens**: desktop and mobile, each opened its own way.
- **Layouts**: Grid and Fluid.
- **Zoom** and **fit**.
- **Groups**.
- **Preview**.
- **Undo**.
- **Drafts**, kept until saved.
- **History** of every saved version.
- **Alarms**, on every measure.
- **Defaults**, shipped with each release.
- **Storage** on the agent, for every client, or on the device.

## Remote access

Not only the WSLC machine is reached from anywhere, but its containers too:
their files, their consoles and their web pages, from a phone, another PC or
across the internet, for you and for your AI agents.

- **Trusted at the machine**, with a login from anywhere else: a user and a
  password, or an API token for scripts and AI agents (2026-09-16).
- **Files** in any container, image or volume of a remote machine: browse,
  edit, copy, upload and download, with the device's own Open and Save
  dialogs. Transfers belong to the agent: they keep going when the window
  closes, and every client sees them.
- **Terminals**: the host's shell and a shell in any container, in the
  browser or in the apps (2026-09-15); on a phone, a row of the keys its
  keyboard lacks, laid out as Termux (2026-10-06).
- **A container's web page**, even one not published: a browser runs on the
  agent's machine and its picture comes to you, with mouse, keyboard, touch
  and clipboard (2026-09-16).
- **No port opened at home**: a guide for reverse SSH tunnels to a VPS.

## Publishing

A container's port on a public HTTPS name, from its own form. The agent
hands it to its proxy, [wslc-published](../plugins/wslc-published/README.md),
nginx and a login in one container, which serves it at once. The installer
offered to set it up from the first release (2026-09-29); on 2026-10-03
the proxy became a plugin of its own, with users per name.

Every published name has a login. An application with a login of its own is
let through; any other gets the proxy's, so nothing goes out to the world
open by mistake.

## Updates that wait for you

The agent updates itself, and it asks first. When a new version arrives, it
tells every client a minute before it starts, and every device that gets
its notifications, with **Cancel update** on the notification itself: any
of them can stop it. It waits for file transfers and backups to finish,
since an update would cut them, then installs, starts again, and the
clients offer their own new version next time they open.

## Compose (October)

`wslc` cannot run a Compose file yet; native support is tracked in
[microsoft/WSL#40948](https://github.com/microsoft/WSL/issues/40948). With
containers, images, volumes, networks and sessions in place, a group of
containers that start together was the next step. On 2026-10-10 Compose
files became projects in the web and the apps, and the same day
`wslc-compose` brought Compose's own command line, in
[1.5.0](https://github.com/Berpiztu/wslc-ai-agent/releases/tag/v1.5.0).

The first real test was humbling. Eight services built from two
Dockerfiles, no cache: Docker did it in 208 seconds, and we took about
1,100. Docker had seen that eight services from two Dockerfiles are two
images with eight names; we had built the same image four times, twice
over. Fixed before lunch, same file, same machine, same morning: 216
seconds, eight behind Docker
([the whole story](features/wslc-compose.md#the-part-where-we-were-slow)).

Three days later a project could be built on another machine from the
sources on yours: the build context travels as a tar archive, and only the
files the agent lacks are sent. A project got its card on the dashboard,
its containers inside it, and a chart could follow a whole project.

## Security (October)

On 2026-10-19 two remote code execution vulnerabilities were published for
ASP.NET Core, which the agent is built on. The patch went out the same day,
and the project changed with it: a library with a known high or critical
vulnerability now stops every build, an audit checks both branches every
morning, and every build asks
[security-watch](https://github.com/Berpiztu/security-watch) first and sends
it its list of libraries after. A release goes out only with a list
security-watch has checked.

The same care then turned to the containers. Every image the agent builds
has its Python libraries checked against OSV.dev, read from the image's own
files without running anything in it, and one moderate or worse stops the
build. Then the check moved to every container: before it is made, by Run,
Create, an edit, an image update or a project's save, and before it starts.
The rule follows who can mend the image:

- **An image built here** is yours to mend: while it is vulnerable, no
  container of it is made or started. Build it again with the version the
  check names.
- **An image from a registry** is not: its containers always start, and are
  marked instead, the state chip saying CVE in yellow or in red, with what
  was found at the end of their logs. Whether it runs is yours to decide.
- **A container that runs** is never stopped by the check.

Each image's libraries and every vulnerability known for them are one click
away, from a container's, a project's or an image's menu, least severe
first.

`wslc` can still be used around the agent, and the agent now notices: a
project with a service whose container is gone, and another container
holding its name, is marked, and answered as an update would be, so nothing
of the project sends its work to a container that is not its own. A project
saved from another Compose file asks first.

## Following WSL

From 2026-10-19 the project has two branches: `main` for the official WSL
release, and `develop`, which follows the newest WSL preview and publishes
its own previews, the first of them 1.6.4, for WSL 3.0.4.
