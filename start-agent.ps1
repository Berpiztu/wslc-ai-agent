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

function Get-DescendantProcessIds {
    param([int]$ParentId)

    $ChildIds = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $ParentId" -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty ProcessId)
    foreach ($ChildId in $ChildIds) {
        Get-DescendantProcessIds -ParentId $ChildId
        $ChildId
    }
}

# Port-in-use check: offer to kill the existing listener first.
$Existing = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($Existing) {
    $OwnerPid = ($Existing | Select-Object -First 1).OwningProcess
    $Owner = Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue
    $DescendantPids = @(Get-DescendantProcessIds -ParentId $OwnerPid)
    $OwnerName = if ($Owner) { $Owner.ProcessName } else { "process already exited" }
    Write-Host "Port $Port is already in use by PID $OwnerPid ($OwnerName)." -ForegroundColor Yellow
    $Answer = Read-Host "Kill PID $OwnerPid and continue? [y/N]"
    if ($Answer -match '^[Yy]') {
        $ProcessIdsToStop = @($DescendantPids) + $OwnerPid | Select-Object -Unique
        Stop-Process -Id $ProcessIdsToStop -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
        $Remaining = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if ($Remaining) {
            Write-Error "Port $Port is still in use by PID $(($Remaining | Select-Object -First 1).OwningProcess); aborting."
        }
        Write-Host "Port $Port is now available." -ForegroundColor Green
    } else {
        Write-Error "Port $Port is in use; aborting."
    }
}

if (-not $NoBuild -and -not $Watch) {
    # In a scope of its own: Packaging.ps1 turns strict mode on for whoever loads it.
    $scope = @(& { . (Join-Path $RepoRoot "packaging\Packaging.ps1"); Get-WslcAgentSolutionScope })
    Write-Host "Building WslcAgent.slnx ($Configuration)..." -ForegroundColor Cyan
    Write-Verbose "dotnet build WslcAgent.slnx -c $Configuration $Output $scope"
    dotnet build (Join-Path $RepoRoot "WslcAgent.slnx") -c $Configuration -nologo @Output @scope
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
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

# The icon beside the clock, for this agent: the solution build above made it,
# except under -Watch, which builds only the agent. It waits for the agent by
# itself, retrying its events stream, so it can start first.
$TrayProcess = $null
if (-not $NoTray) {
    $TrayProject = Join-Path $RepoRoot "src\WslcAgent.Tray"
    if ($Watch -and -not $NoBuild) {
        Write-Host "Building WslcAgent.Tray ($Configuration)..." -ForegroundColor Cyan
        Write-Verbose "dotnet build $TrayProject -c $Configuration $Output"
        dotnet build $TrayProject -c $Configuration -nologo @Output
        if ($LASTEXITCODE -ne 0) { throw "dotnet build of the tray failed with exit code $LASTEXITCODE" }
    }

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
