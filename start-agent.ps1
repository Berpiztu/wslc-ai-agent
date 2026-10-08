<#
.SYNOPSIS
    Build and start WSLC AI Agent for local development.
.DESCRIPTION
    Builds the solution (unless -NoBuild), frees the port if you agree, then
    runs WslcAgent.Server in the Development environment, which serves the
    Blazor UI from the build output, on http://127.0.0.1:8070 by default.
    -Watch runs it under dotnet watch instead (Hot Reload on save; it restarts
    the agent by itself when a change needs it, so the console is noisier).
    The agent's icon beside the clock starts too, pointed at this agent, so
    its notifications are tried without installing; it is closed, by its
    PID, when the agent stops (-NoTray leaves it out). The installed agent's
    icon, if running, stays.
    Works from any current directory: it always runs from the repository root.
.EXAMPLE
    .\start-agent.ps1
.EXAMPLE
    .\start-agent.ps1 -Port 5200
.EXAMPLE
    .\start-agent.ps1 -Watch      # dotnet watch: hot reload on save
.EXAMPLE
    .\start-agent.ps1 -NoTray     # the agent alone, without its icon beside the clock
.EXAMPLE
    .\start-agent.ps1 -Quiet      # a line per step: no build log, the agent at its own log levels
#>
[CmdletBinding()]
param(
    [string]$BindHost = "127.0.0.1",
    [int]$Port = 8070,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$Watch,
    [switch]$NoTray,
    [switch]$Quiet
)

# Verbose unless asked not to: every command, dotnet's whole build log and
# the agent's debug log are what the owner reads a run by.
if (-not $Quiet) { $VerbosePreference = "Continue" }

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot

$Project = Join-Path $RepoRoot "src\WslcAgent.Server"
$Url = "http://${BindHost}:${Port}"
# Verbose (unless -Quiet): MSBuild's whole log — detailed, every project, target and task —
# without the terminal logger, which folds any verbosity into a line per
# project; and the agent itself at Debug as it runs.
$Verbose = $VerbosePreference -eq "Continue"
$Output = if ($Verbose) { @("-v", "detailed", "--tl:off") } else { @("-v", "q") }

# Never PID 0: the System Idle Process is listed as its own child, and asking
# for its children went round for ever, the script ending without starting the
# agent. A process already gone through is not gone through again.
function Get-DescendantProcessIds {
    param([int]$ParentId, [System.Collections.Generic.HashSet[int]]$Seen = [System.Collections.Generic.HashSet[int]]::new())

    if ($ParentId -le 0 -or -not $Seen.Add($ParentId)) { return }
    $ChildIds = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentId" -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty ProcessId)
    foreach ($ChildId in $ChildIds) {
        if ($ChildId -le 0 -or $Seen.Contains($ChildId)) { continue }
        Get-DescendantProcessIds -ParentId $ChildId -Seen $Seen
        $ChildId
    }
}

# The process listening on the port, or 0 when none is named. Neither
# Get-NetTCPConnection nor netstat is steady here: measured, the installed
# agent, alive and listening, was in one look in two, and a listener is named
# PID 0 now and then. So both are asked, a few times, and the first process
# named answers.
function Get-PortOwner {
    param([int]$Port)

    for ($Look = 0; $Look -lt 6; $Look++) {
        $Named = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
            Where-Object OwningProcess -gt 0 | Select-Object -First 1
        if ($Named) { return [int]$Named.OwningProcess }
        $Line = netstat -ano -p TCP | Select-String -Pattern ":$Port\s+\S+\s+LISTENING\s+([1-9]\d*)" | Select-Object -First 1
        if ($Line) { return [int]$Line.Matches[0].Groups[1].Value }
        Start-Sleep -Milliseconds 100
    }
    return 0
}

# The live process listening on the port, or nothing: none, or only the
# listeners of a process already gone.
function Get-PortProcess {
    param([int]$Port)

    $OwnerPid = Get-PortOwner -Port $Port
    if ($OwnerPid -le 0) { return $null }
    return Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue
}

# Port-in-use check. A live process listening is offered to be stopped first.
# Listeners left by an agent that stopped, which Windows names as nobody's
# (PID 0) and clears in its own time, do not keep the agent from listening
# again — measured: the agent ran on this port beside two of them —, so they
# are let be and the agent starts; asking to kill PID 0 only aborted the start.
$Owner = Get-PortProcess -Port $Port
if ($Owner) {
    $DescendantPids = @(Get-DescendantProcessIds -ParentId $Owner.Id)
    Write-Host "Port $Port is already in use by PID $($Owner.Id) ($($Owner.ProcessName))." -ForegroundColor Yellow
    $Answer = Read-Host "Kill PID $($Owner.Id) and continue? [y/N]"
    if ($Answer -notmatch '^[Yy]') {
        Write-Error "Port $Port is in use; aborting."
    }

    Stop-Process -Id (@($DescendantPids) + $Owner.Id | Select-Object -Unique) -Force -ErrorAction SilentlyContinue
    # The process ends a moment after it is told to: up to five seconds, asked every quarter of one.
    for ($Wait = 0; $Wait -lt 20 -and (Get-PortProcess -Port $Port); $Wait++) {
        Start-Sleep -Milliseconds 250
    }
    if (Get-PortProcess -Port $Port) {
        Write-Error "Port $Port is still in use by PID $((Get-PortProcess -Port $Port).Id); aborting."
    }
    Write-Host "Port $Port is now available." -ForegroundColor Green
} elseif (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "Port $Port still lists listeners of a stopped agent, which Windows clears by itself; starting anyway." -ForegroundColor DarkGray
}

# Only what the agent runs on: the agent, which builds the UI it serves with
# it, and the icon beside the clock. Not the whole solution: its Android
# client made an APK at every start, and the Windows client and the tests
# took their time too, none of them run here (debug-android.ps1 and
# build-client-apk.ps1 make the APK; build.ps1 builds and tests the whole).
if (-not $NoBuild) {
    $Builds = @($Project) + $(if ($NoTray) { @() } else { @(Join-Path $RepoRoot "src\WslcAgent.Tray") })
    foreach ($Built in $Builds) {
        if ($Watch -and $Built -eq $Project) { continue }
        Write-Host "Building $(Split-Path -Leaf $Built) ($Configuration)..." -ForegroundColor Cyan
        Write-Verbose "dotnet build $Built -c $Configuration $Output"
        dotnet build $Built -c $Configuration -nologo @Output
        if ($LASTEXITCODE -ne 0) { throw "dotnet build of $(Split-Path -Leaf $Built) failed with exit code $LASTEXITCODE" }
    }
}

# Development: the referenced projects' static assets (the Blazor UI) are
# served from the build output. A Production run needs `dotnet publish`.
$env:ASPNETCORE_ENVIRONMENT = "Development"
if ($Verbose) {
    # The agent's own log at Debug, ASP.NET Core's at Information (its Debug is
    # every request's every step), over appsettings.Development.json.
    $env:Logging__LogLevel__Default = "Debug"
    ${env:Logging__LogLevel__Microsoft.AspNetCore} = "Information"
    Write-Verbose "Agent log levels: Default=Debug, Microsoft.AspNetCore=Information"
}

# The icon beside the clock, for this agent, built above. It waits for the
# agent by itself, retrying its events stream, so it can start first.
$TrayProcess = $null
if (-not $NoTray) {
    $TrayProject = Join-Path $RepoRoot "src\WslcAgent.Tray"

    $TrayExe = Join-Path $TrayProject "bin\$Configuration\net10.0-windows10.0.19041.0\wslc-ai-agent-tray.exe"
    if (-not (Test-Path -LiteralPath $TrayExe)) { throw "The tray is not built: $TrayExe" }
    $TrayProcess = Start-Process -FilePath $TrayExe -ArgumentList "--agent", "$Url/" -WorkingDirectory (Split-Path -Parent $TrayExe) -PassThru
    Write-Host "Tray icon started for $Url (PID $($TrayProcess.Id))" -ForegroundColor Green
}

Write-Host "Starting WSLC AI Agent on $Url (Ctrl+C to stop)" -ForegroundColor Green
try {
    if ($Watch) {
        Write-Verbose "dotnet watch --project $Project run -c $Configuration -- --urls $Url"
        dotnet watch --project $Project run -c $Configuration -- --urls $Url
    } else {
        Write-Verbose "dotnet run --project $Project -c $Configuration --no-build -- --urls $Url"
        dotnet run --project $Project -c $Configuration --no-build -- --urls $Url
    }
} finally {
    # Only the icon this script started, by its PID: the installed agent's
    # icon runs the same program and must stay.
    if ($TrayProcess -and -not $TrayProcess.HasExited) {
        Stop-Process -Id $TrayProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
