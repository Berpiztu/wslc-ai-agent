# security-watch

[Berpiztu/security-watch](https://github.com/Berpiztu/security-watch)
checks the libraries Berpiztu's projects use against the known
vulnerabilities and the newest versions, each library once for all the
projects that use it. It reads no project: each one sends its own list of
libraries, and reads its own result.

This repository does both from its builds ([packaging/SecurityWatch.ps1](../../packaging/SecurityWatch.ps1)):
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
`develop`), before it commits anything.

## After a build

A build that succeeded makes this checkout's list: every NuGet package of
every project restored here at the version it resolved (the transitive ones
included), the central version of the ones not restored here (the MAUI
client without its workloads), the pinned lines of every tracked
requirements file, the .NET SDK of `global.json`, and the images of every
Dockerfile and Compose file. It sends it to
`inventories/wslc-ai-agent/<ref>.json` only when it differs from the list
security-watch already has, and only for `main`, `develop` and release
tags. security-watch checks it within minutes, so the next build already
knows.

## The token

Sending needs `SECURITY_WATCH_TOKEN`, a token that writes to
security-watch alone: GitHub → Settings → Developer settings →
Fine-grained tokens → Generate new token, resource owner **Berpiztu**,
repository access **Only select repositories: security-watch**, permission
**Contents: Read and write**.

- On a machine: in `private\env.psd1` ([private-files.md](private-files.md#settings-envpsd1)).
- On GitHub: the repository secret `SECURITY_WATCH_TOKEN`, which `ci.yml`
  and `release.yml` pass to the builds.

Without it the build still asks, and says the list was not sent. Asking
needs no token: the status is public.
