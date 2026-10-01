# Getting started

From a Windows machine with nothing of this project on it to the agent
running from source, the tests green and, if you want them, the installers
built. Every step says what you should see; when you see something else, the
step says what to do.

The short version is in the [README](../../README.md#quick-start). This is
the same path, one step at a time.

## 1. Allow PowerShell to run the repository's scripts

Everything is driven by PowerShell scripts at the root of the repository.
Windows PowerShell refuses to run scripts until you allow it, once, for your
user:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

Answer `Y`. PowerShell 7 (`pwsh`) already allows it; the scripts work in
both.

## 2. Clone the repository

```powershell
git clone https://github.com/berpiztu/wslc-ai-agent.git
```

Then go into it; every later command runs from there:

```powershell
cd wslc-ai-agent
```

No git yet? `winget install --id Git.Git -e`, then open a new terminal.

**Downloaded the ZIP instead of cloning?** Windows marks every file that
comes out of a ZIP downloaded from the internet, and with the policy of step 1
(`RemoteSigned`) PowerShell refuses to run a marked script that is not
signed: `check-prereqs.ps1 cannot be loaded … is not digitally signed`. A
clone carries no such mark. Remove it once, from the folder you unzipped:

```powershell
Get-ChildItem -Recurse | Unblock-File
```

## 3. Check the prerequisites

```powershell
.\check-prereqs.ps1
```

It looks at what this machine has and changes nothing. Each line is one of
the following, and every line that is not `[ok]` is followed by `run:`, the
command that installs it on a line of its own, and `how:`, the section of
[prerequisites.md](prerequisites.md) that explains it (for VS Code, Visual
Studio or no IDE at all):

- `[ok]`: present.
- `[absent]`: missing, and only the part it names is switched off (the APK,
  the Windows client, containers to manage).
- `[broken]`: missing, and nothing builds without it.

To install everything it reports missing in one go:

```powershell
.\install-prereqs.ps1
```

Windows asks once for administrator rights, and the script goes on in an
administrator window of its own, which stays open to read. It runs the
printed commands in order (the .NET SDK before its workloads, the JDK and the
workloads before the Android SDK), reloading the PATH between them so each
sees what the one before installed, then checks again. A command that fails
is reported and not run twice.

What you need at the least, and how to install it:

| Prerequisite | Install |
|---|---|
| Windows 11 (Windows 10 builds everything, but WSLC runs on Windows 11) | — |
| The .NET SDK that `global.json` names, or a later feature band of it | `winget install --id Microsoft.DotNet.SDK.10 -e` |
| The `android` and `maui-blazor` workloads | `dotnet workload install maui-windows maui-android`, from a PowerShell run as administrator |
| Access to nuget.org (the first build downloads every package, WiX included) | — |

And for the parts that need them:

| Prerequisite | For | Install |
|---|---|---|
| Android SDK | The APK, `debug-android.ps1` | [With VS Code or no IDE, Android Studio or Visual Studio](prerequisites.md#android-sdk) |
| A JDK (`keytool`) | The APK and its signing key | `winget install --id Microsoft.OpenJDK.17 -e`, or set `JAVA_HOME` |
| WebView2 Runtime | The Windows client, the tray window | `winget install --id Microsoft.EdgeWebView2Runtime -e` |
| WSL, with virtualization turned on | Running containers at all | [prerequisites.md](prerequisites.md#wsl): `wsl --install --no-distribution` as administrator, then restart |
| WSLC **3.0.1 or later** recommended, 2.9.13 at the least (`wslc` on the PATH) | The agent's containers | The [latest WSL release](https://github.com/microsoft/WSL/releases/latest), then open a new terminal |

The agent works with WSLC 2.9.13 or later; 3.0.1 is the first generally
available release and the one to install. An older `wslc` is reported as
`[broken]` with the link to update it. `wslc version` prints the one you have.

Whether you installed with `install-prereqs.ps1` or command by command,
**close every terminal and VS Code and open them again** afterwards (a terminal only sees what was installed before it, or
before VS Code, started), and run the script again until the last line is
green: **Ready to build**.

## 4. Check the private files (optional)

```powershell
.\check-private.ps1
```

The signing key of the Android app and the two Firebase files of push
notifications are secrets, so they are not in the repository. A fresh clone
has none of them, and that is fine: everything builds, runs and tests
without them. On a fresh clone you should see every file `[absent]` and the
last line green: **Nothing broken**.

When you want signed APKs or push notifications,
[private-files.md](private-files.md) says how to make each file, and this
script tells you whether each one is right.

## 5. Build and test

```powershell
.\build.ps1
```

It restores the packages, builds the whole solution and runs the tests: the
same steps CI runs. The first run downloads the packages and takes a few
minutes. You should see `Restoring`, `Building`, `Testing`, the test summary
with no failure, and **Done.** in green.

`.\build.ps1 -Configuration Release` matches CI exactly. CI builds in two
parts: every push runs `-NoClient` (everything but the MAUI client, with no
workloads to install, in a few minutes; `ci.yml`), and the client is built
when it, or how everything is built, changes, and by hand from the Actions
tab before a release (`client.yml`).

## 6. Run the agent

```powershell
.\start-agent.ps1
```

It builds and starts the agent on <http://127.0.0.1:8070>, with its icon
beside the clock. Open that address in a browser: the Home dashboard shows,
and Containers, Images, Volumes and Networks list what WSLC has. Stop it with
`Ctrl+C`.

`-Watch` reloads the agent on every save; `-Port` changes the port;
`-NoTray` leaves the icon out.

## 7. Connect an AI assistant (optional)

The agent serves an MCP endpoint at `/api/v1/mcp`. With the agent running,
from Claude Code:

```powershell
claude mcp add --transport http wslc-agent http://127.0.0.1:8070/api/v1/mcp
```

Destructive tools ask for your approval before they act.

## 8. Run the clients (optional)

With the agent running, in another terminal.

The Windows client, pointed at http://127.0.0.1:8070/:

```powershell
.\debug-client.ps1
```

The Android client on an emulator:

```powershell
.\debug-android.ps1
```

Or on a phone attached by USB, with USB debugging on:

```powershell
.\debug-android.ps1 -Phone
```

`debug-android.ps1` starts an emulator when no device is attached (create one
in Android Studio's Device Manager first). From the emulator, the PC is
`10.0.2.2`, which is the default agent address it passes.

## 9. Build the installers (optional)

The agent, `dist\wslc-ai-agent.msi`:

```powershell
.\build-agent-installer.ps1 -NoBump
```

The Windows client, `dist\wslc-ai-client.msi`:

```powershell
.\build-client-installer.ps1 -NoBump
```

The Android client, `dist\wslc-ai-client.apk`:

```powershell
.\build-client-apk.ps1 -NoBump
```

Each script raises the version at every build, in `private\version.props`,
which git never tracks: your builds change no tracked file. `-NoBump` builds
the same version again.

The agent MSI installs per user under `%LOCALAPPDATA%\WSLC-AI-Agent`, runs on
<http://127.0.0.1:8069> by default and starts at every logon. The APK is
signed with your key from `private\` (generated there the first time), or
see [private-files.md](private-files.md).

## When something fails

1. Run `.\check-prereqs.ps1` again: most failures are a missing SDK or
   workload.
2. A build that fails after an SDK or workload change: close every terminal
   and VS Code, open them again, and build again.
3. Still failing: open an issue with the output of both check scripts (they
   print no secret) and of the failing command.
