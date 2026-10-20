<#
.SYNOPSIS
    Build wslc-compose as one file to copy anywhere: dist\wslc-compose.exe.
.DESCRIPTION
    Publishes src\Berpiztu.Wslc.Compose self-contained for win-x64 as a single
    file, with the .NET runtime inside it: a machine needs nothing else to run
    it, not the agent either. It is the command for a machine that has no
    agent of its own and talks to another one's (--agent, --token), and a way
    to try the command without installing anything.
    The agent's installer does not use this file: build-agent-installer.ps1
    publishes the command beside the agent, on the agent's runtime.
    It carries the agent's version, the one the last build-agent-installer.ps1
    left (private\version.props), and bumps nothing: the command is versioned
    with the agent it is released with.
    -Clean deletes previous outputs. It shows what it does: the command it
    runs and dotnet's whole log. -Quiet keeps to a line per step.
#>
[CmdletBinding()]
param(
    [switch]$Clean,
    [switch]$Quiet
)

# Verbose unless asked not to: the command and dotnet's whole log are what the
# owner reads a build by (Get-WslcAgentDotnetOutput follows this).
if (-not $Quiet) { $VerbosePreference = "Continue" }

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

# security-watch first: a library this build would use with a known
# vulnerability, moderate or worse, stops it before anything is built.
Test-WslcAgentSecurity

Import-WslcAgentPrivateSettings

$Project = Join-Path $RepoRoot "src\Berpiztu.Wslc.Compose\Berpiztu.Wslc.Compose.csproj"
$Stage = Join-Path $RepoRoot "dist\wslc-compose-payload"
$ExeDest = Join-Path $RepoRoot "dist\wslc-compose.exe"

if ($Clean -and (Test-Path -LiteralPath $ExeDest)) { Remove-Item -Force -LiteralPath $ExeDest }
if (Test-Path -LiteralPath $Stage) { Remove-Item -Recurse -Force -LiteralPath $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

$version = Update-WslcAgentVersion -NoBump
$Output = @(Get-WslcAgentDotnetOutput)

# The native libraries go inside the file too: without them a single file is
# a file and a handful of DLLs beside it.
Write-Host "Publishing wslc-compose $version (one file, self-contained win-x64)..." -ForegroundColor Cyan
Write-Verbose "dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true $Output -o $Stage"
& dotnet publish $Project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -nologo @Output -o $Stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
$built = Join-Path $Stage "wslc-compose.exe"
if (-not (Test-Path -LiteralPath $built)) { throw "Publish produced no wslc-compose.exe in $Stage" }

Copy-Item -LiteralPath $built -Destination $ExeDest -Force
Remove-Item -Recurse -Force -LiteralPath $Stage

$size = [math]::Round((Get-Item -LiteralPath $ExeDest).Length / 1MB, 1)
Write-Host ""
# Built: security-watch gets this checkout's list of libraries when it changed.
Send-WslcAgentInventory
Write-Host "wslc-compose ready: $ExeDest (version $version, $size MB)" -ForegroundColor Green
Write-Host "Try it against this machine's agent: `"$ExeDest`" ls"
Write-Host "Against another one: `"$ExeDest`" --agent http://host:8069 --token TOKEN ls"
