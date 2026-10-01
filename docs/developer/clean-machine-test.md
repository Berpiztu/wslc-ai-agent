# Testing on a clean machine

The README's Quick start has to work for someone who has only the clone. Your
own machine cannot prove it: its SDKs, workloads, keys and settings help
without being written down. Windows Sandbox gives a clean Windows in seconds,
isolated from yours, and erased when it closes.

## What it covers

| Covered | Not covered, and why |
|---|---|
| Installing the prerequisites the way the docs say | WSLC: the Sandbox has no nested virtualization, so `check-prereqs.ps1` reports it `[absent]`, as on a machine without WSLC |
| Cloning, both check scripts, `build.ps1` and its tests | The Android emulator, for the same reason (a phone on USB is not passed through either) |
| Running the agent and opening its page | |
| The Windows client, the tray icon, installing and uninstalling the MSIs | |

The full test with WSLC runs on a virtual machine or a second computer.
Every push is also built on a clean machine by CI (`.github/workflows/ci.yml`,
without the MAUI client; `client.yml` builds the client when it changes),
through the same scripts.

## Enable Windows Sandbox (once)

It comes with Windows 11 Pro, Enterprise and Education; Home does not have
it. Virtualization has to be on in the firmware (it is when WSL works).

In a PowerShell **run as administrator**:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All
```

Then restart Windows.

## Run the test

From the root of your checkout:

```powershell
.\start-sandbox.ps1
```

It opens Windows Sandbox with 16 GB of memory (`-MemoryGB` changes it). At
logon the Sandbox installs winget, which a bare Sandbox lacks and every
Windows 11 has, and nothing else. It takes a minute or two; a log is left on
the Sandbox's desktop. Then a terminal opens that shows the clone URL of your
checkout's repository.

In that terminal, follow the [README's Quick start](../../README.md#quick-start)
from step 1, exactly as written, copying each line from the page:

- Take nothing from your own machine: no file, no key, no command the docs do
  not give.
- When a step needs something the docs do not say, that is the finding: write
  it down, it is fixed in the docs.
- `check-private.ps1` should report every file `[absent]`: a new user has no
  private files.
- A private repository asks git to sign in to GitHub; the browser opens in
  the Sandbox for it.

Closing the Sandbox window erases everything in it. Each run starts clean.

## What to check

1. Every step of the Quick start ends as it says it should.
2. `check-prereqs.ps1` points each missing piece to its section of
   [prerequisites.md](prerequisites.md), and following that section fixes it.
3. The agent's page opens at http://127.0.0.1:8070.
4. `git status --ignored` in the clone shows nothing modified by the build.
