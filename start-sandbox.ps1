<#
.SYNOPSIS
    Open a clean Windows in Windows Sandbox to try the README's Quick start as
    a new user would.
.DESCRIPTION
    Writes a Windows Sandbox configuration to dist\sandbox\ and opens it. At
    logon the Sandbox installs winget (tests\sandbox\bootstrap.ps1), which a
    bare Sandbox lacks and every Windows 11 has, and opens a terminal that
    shows the clone URL of this repository. Nothing of this machine is shared:
    no SDK, no private file, no key. Closing the Sandbox erases everything.
    WSLC and the Android emulator do not run in the Sandbox (it has no nested
    virtualization); check-prereqs.ps1 reports them as absent there.
    docs\developer\clean-machine-test.md says how to enable the Sandbox and
    what to check.
.EXAMPLE
    .\start-sandbox.ps1
.EXAMPLE
    .\start-sandbox.ps1 -MemoryGB 12
#>
param(
    # The build of the whole solution with its MAUI clients needs room.
    [int]$MemoryGB = 16
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

$sandboxExe = Join-Path $env:WINDIR "System32\WindowsSandbox.exe"
if (-not (Test-Path -LiteralPath $sandboxExe)) {
    throw "Windows Sandbox is not enabled. Enable it as administrator and restart: docs\developer\clean-machine-test.md"
}

$work = Join-Path $RepoRoot "dist\sandbox"
New-Item -ItemType Directory -Force -Path $work | Out-Null
Copy-Item -LiteralPath (Join-Path $RepoRoot "tests\sandbox\bootstrap.ps1") -Destination $work -Force
$cloneUrl = (git -C $RepoRoot remote get-url origin)
if (-not $cloneUrl) { throw "This checkout has no origin remote to show as the clone URL." }
Set-Content -LiteralPath (Join-Path $work "clone-url.txt") -Value $cloneUrl -NoNewline

$config = @"
<Configuration>
  <MemoryInMB>$($MemoryGB * 1024)</MemoryInMB>
  <Networking>Enable</Networking>
  <vGPU>Enable</vGPU>
  <ClipboardRedirection>Enable</ClipboardRedirection>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$work</HostFolder>
      <SandboxFolder>C:\sandbox-setup</SandboxFolder>
      <ReadOnly>true</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <LogonCommand>
    <Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\sandbox-setup\bootstrap.ps1</Command>
  </LogonCommand>
</Configuration>
"@
$wsb = Join-Path $work "clean-machine.wsb"
Set-Content -LiteralPath $wsb -Value $config -Encoding UTF8

Write-Host "Opening Windows Sandbox ($MemoryGB GB). It installs winget, then opens a terminal for the Quick start." -ForegroundColor Cyan
Start-Process -FilePath $wsb
