# AGENTS.md

Instructions for AI coding agents (Claude Code, Codex, Copilot and others)
working in this repository. People: the same rules are in
[CONTRIBUTING.md](CONTRIBUTING.md).

## What this is

WSLC AI Agent: a Windows agent for WSLC (Microsoft's `wslc` container CLI)
that serves a web UI, Windows and Android clients, a tray icon and an MCP
server. .NET 10, ASP.NET Core minimal APIs, Blazor WebAssembly with
MudBlazor, .NET MAUI Blazor Hybrid, the official MCP C# SDK.
[docs/architecture.md](docs/architecture.md) maps the projects.

## Build, run, test

- `.\check-prereqs.ps1`: what the machine lacks. `.\install-prereqs.ps1`
  installs it.
- `.\build.ps1`: restore, build, test, as CI does. Run it before proposing a
  change.
- `.\start-agent.ps1`: the agent in Development on http://127.0.0.1:8070.
- Tests are quick smoke tests per area, in `tests/`.

## Rules

- **English** for code, comments, docs and commit messages.
- **WSLC, never Docker.** The agent runs `wslc`. Never call `docker`, never
  name anything after Docker.
- **One UI.** The Razor components in `src/WslcAgent.UI` are the only user
  interface; the web app, the clients and the tray host them. Native code
  only for what a WebView cannot do.
- **MudBlazor with its own look.** Use its components and idioms. No visual
  restyling in CSS; the stylesheets hold layout MudBlazor lacks.
- **One API.** A new capability is an endpoint in `/api/v1`, documented in
  [docs/api-v1.md](docs/api-v1.md), and a matching MCP tool where it makes
  sense, both over the same service interface.
  Every change to it raises `ApiCompatibility.Level`
  (`src/WslcAgent.ApiClient/ApiCompatibility.cs`); one that breaks older
  clients (something removed, renamed or made required) also raises
  `MinimumClientLevel`, and one a client cannot do without on an older agent
  raises `MinimumAgentLevel`. A change that only adds raises neither. Native
  clients check these against `/health` and stop, or warn, when either side
  is behind.
- **Destructive MCP tools** (remove, prune, kill, exec, publish, stop a
  session) go through the approval gate and are off unless the user switches
  them on. Keep it that way for any new one.
- **Reuse.** One component per concept: one list-page shell
  ([docs/list-pages.md](docs/list-pages.md)), one picker and one creator per
  resource. Extract duplicated logic before it appears a third time.
- **Controls** take the parameters in [docs/ui-controls.md](docs/ui-controls.md).
  Type sizes and form fields follow [docs/RULES.md](docs/RULES.md); do not
  change that scale or the CSS behind fields without an agreed issue.
- **One design for every screen size.** Fluid layouts; no second set of
  paddings behind a breakpoint.
- **Back is navigation.** What a screen shows (tab, search, view, page) lives
  in the URL.
- **Unsaved work asks before leaving** (a location-changing handler).
- **Clean code.** Small units, names that say what they do, comments that
  explain why, no dead or commented-out code, no temporary hacks.
- **No secrets, nothing machine-specific.** Keys and Firebase files live in
  `private/`, which git ignores.

## Before you finish

Build and test with `.\build.ps1`, update the docs the change touches, and
keep the change to one concern.
