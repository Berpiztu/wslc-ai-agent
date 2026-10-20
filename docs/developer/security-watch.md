# security-watch

[Berpiztu/security-watch](https://github.com/Berpiztu/security-watch)
checks the libraries Berpiztu's projects use against the known
vulnerabilities and the newest versions, each library once for all the
projects that use it. It reads no project: each one sends its own list of
libraries, and reads its own result.

This repository does both from its builds with
[packaging/SecurityWatch.ps1](../../packaging/SecurityWatch.ps1), a copy of
security-watch's [client/SecurityWatch.ps1](https://github.com/Berpiztu/security-watch/blob/main/client/SecurityWatch.ps1),
the same in every project (a change there is copied here):
`build.ps1`, `build-agent-installer.ps1`, `build-client-installer.ps1`,
`build-client-apk.ps1` (and `-full`) and `build-wslc-compose.ps1`, on this
machine and on GitHub Actions. `start-agent.ps1` and the `debug-*.ps1`
scripts do not.

## Before a build

The build asks first: `status/wslc-ai-agent.json`, for the branch it is on
(or the release tag, on a release's build on GitHub).

- A library with a vulnerability **moderate or worse**, or a .NET SDK
  whose runtime a .NET security release has left behind, **stops the
  build**, naming each one, its advisory and the version that fixes it:
  that is a [security patch](releasing.md#a-security-patch).
- A **low** one, or one of unknown severity, is written as a warning, and
  the build goes on.
- When security-watch **cannot be reached**, the build asks whether to go
  on. On GitHub Actions, with nobody to ask, it stops.
- A branch security-watch has no list of yet builds, and its list is sent
  after.

`deploy-release.ps1` asks too, for the branch it releases (`main` or
`develop`), before it commits anything, and more: what it is about to
release must be the list security-watch checked. The list this checkout
builds is compared with the one security-watch holds; one the build sent
and security-watch has not answered yet is waited for (three minutes at
most), and no list at all stops the release: **build first, then
release**.

## After a build

A build that succeeded makes this checkout's list: every NuGet package of
every project restored here at the version it resolved (the transitive ones
included), the central version of the ones not restored here (the MAUI
client without its workloads), the pinned lines of every tracked
requirements file, the .NET SDK of `global.json`, and the images of every
Dockerfile and Compose file. It sends it only when it differs from the
list security-watch already has (`inventories/wslc-ai-agent/<ref>.json`),
and only for `main`, `develop` and release tags, as an **issue** in
security-watch titled `inventory: wslc-ai-agent@<ref>`, the list in a
```json block. security-watch takes it in, checks it, answers the issue
with the result and closes it, in under a minute. The build waits for that
answer (three minutes at most) and says it before it ends: nothing that
stops a build, or each library moderate or worse with the version that
fixes it, and the build fails.

## Who sends

No token of its own: the issue is opened by whoever `gh` is signed in as on
this machine (`gh auth login`), as they would open it by hand. security-watch
takes it only from the accounts in its `submitters.txt`, for the projects in
its `projects.txt`: the repository is public, and an inventory issue from
anyone else is refused. Nobody writes a file in security-watch but its own
watcher.

- On a machine where `gh` is not signed in, the build says the list was not
  sent, and goes on.
- **GitHub Actions only asks.** The lists of `main` and `develop` come from
  the builds made on a machine; `deploy-release.ps1` sends each release's
  list when it tags it.

Asking needs no sign-in at all: the status is public.
