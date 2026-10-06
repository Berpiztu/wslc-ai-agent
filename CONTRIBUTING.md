# Contributing to WSLC AI Agent

Thank you for helping. Issues, ideas and pull requests are all welcome. This
page says how to get the code running, what a pull request needs, and the
few rules the code base keeps.

## Get it running

Follow [Build it yourself](README.md#build-it-yourself) in the README:
`check-prereqs.ps1` says what your machine lacks, `install-prereqs.ps1`
installs it, `build.ps1` builds and tests, `start-agent.ps1` runs the agent
on http://127.0.0.1:8070. Every step, and what to do when one shows something
else: [docs/developer/getting-started.md](docs/developer/getting-started.md).

You need Windows 11 and WSLC to run the agent against containers. Everything
builds and the tests pass without WSLC.

## Before you start

- **Bugs**: open an issue with the steps, what you expected and what
  happened, and the versions (Windows, `wslc version`, and the agent's, which
  http://127.0.0.1:8069/api/v1/health reports).
- **Features and larger changes**: open an issue first and describe the idea,
  so we can agree on the approach before you spend time on it.
- **Security problems**: never in a public issue. See [SECURITY.md](SECURITY.md).

## Pull requests

1. Fork the repository and branch from `main`.
2. Keep a pull request to one concern: a fix, a feature, a refactor. Several
   small pull requests are reviewed faster than one large one.
3. Run `.\build.ps1` before pushing: it builds everything and runs the tests,
   as CI does. A pull request is merged only with CI green.
4. Update the documentation the change touches, in the same pull request:
   - a new or changed endpoint in [docs/api-v1.md](docs/api-v1.md), and its
     MCP tool where one makes sense;
   - a list page or a control in [docs/list-pages.md](docs/list-pages.md) and
     [docs/ui-controls.md](docs/ui-controls.md);
   - a user-visible feature in [docs/features.md](docs/features.md).
5. Describe what changed and why, and how you tested it. Screenshots help for
   anything visible.
6. Commit messages are in English, with a short subject line that says what
   the commit does.

Pull requests are squash-merged into `main`.

## The rules the code keeps

The full set is in [docs/architecture.md](docs/architecture.md) and
[docs/RULES.md](docs/RULES.md). The ones a first contribution meets:

- **English everywhere**: code, comments, docs and commit messages.
- **One UI.** The Razor components in `WslcAgent.UI` are the only user
  interface: the web app, the Windows and Android clients and the tray all
  host them. Native code is only for what a WebView cannot do.
- **MudBlazor, its own look.** Layout, tables, dialogs, forms and snackbars are
  MudBlazor components. No custom visual CSS beyond the theme; the stylesheets
  in `wwwroot` hold layout MudBlazor lacks.
- **One API.** Everything goes through `/api/v1`, documented in
  [docs/api-v1.md](docs/api-v1.md). Every endpoint gets a matching MCP tool
  where it makes sense, both over the same service.
  Every change to it raises `ApiCompatibility.Level`
  (`src/WslcAgent.ApiClient/ApiCompatibility.cs`); one that breaks older
  clients (something removed, renamed or made required) also raises
  `MinimumClientLevel`, and one a client cannot do without on an older agent
  raises `MinimumAgentLevel`. A change that only adds raises neither. Native
  clients check these against `/health` and stop, or warn, when either side
  is behind.
- **Reuse before you add.** One component per concept (one list-page shell,
  one details view, one picker per kind of resource), fed different data.
  When two places do the same thing, extract it before a third appears.
- **Clean code.** Small units with one job, names that say what they do,
  comments that explain why rather than what, no dead or commented-out code.
- **Type sizes and form fields** follow a fixed scale
  ([docs/RULES.md](docs/RULES.md)). A change to that scale, or to the CSS behind
  fields, is agreed in an issue first.
- **Never `docker`.** The product is WSLC: the agent runs `wslc`, nothing else.
- **No secrets and nothing machine-specific** in the repository. Signing keys
  and Firebase files live in `private/`, which git ignores
  ([docs/developer/private-files.md](docs/developer/private-files.md)).

## AI-assisted contributions

Contributions written with AI agents are welcome, under the same rules and
the same review. [AGENTS.md](AGENTS.md) gives agents the repository's rules
in one place. You are responsible for what you submit: read it, build it and
test it before opening the pull request.

## Code of conduct

Everyone taking part is expected to follow the
[code of conduct](CODE_OF_CONDUCT.md).

## License

By contributing you agree that your contribution is licensed under the
project's [MIT License](LICENSE).
