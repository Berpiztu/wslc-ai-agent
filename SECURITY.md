# Security policy

WSLC AI Agent runs containers on your machine, can be reached from the
internet, and lets AI agents act on your behalf. We take reports about it
seriously.

## Reporting a vulnerability

**Please do not open a public issue.** Report it privately through GitHub:
the repository's **Security** tab → **Report a vulnerability**. Include what
is affected, how to reproduce it, and what an attacker could do with it.

We will acknowledge the report, keep you informed while we work on it, and
credit you in the release that fixes it unless you prefer otherwise.

## Supported versions

Security fixes go into the latest release. Update through Settings → Update,
or by installing the latest release ([docs/updating.md](docs/updating.md)).

## What is in scope

Among others:

- The agent's authentication: the Internet login, API tokens, and how it
  tells a local caller from a remote one behind a proxy.
- The MCP server: a tool acting without the user's approval when it should
  ask, or a destructive tool reachable while destructive tools are off.
- Publishing: a container reachable from the internet without having been
  published.
- The host browser, the terminals, file transfers and the saved logins.
- The installers and the self-update.

A container you published yourself answers with whatever that container
does: its own security is out of scope
([docs/developer/remote-access.md](docs/developer/remote-access.md#security)).
