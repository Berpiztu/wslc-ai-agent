<#
.SYNOPSIS
    Build and test wslc-agent, the same steps CI runs.
.DESCRIPTION
    Restores, builds WslcAgent.slnx and runs the tests. Works from any
    current directory. Use -NoTest to build only and -Configuration Release
    to match CI exactly. -NoClient leaves out the MAUI client (Windows and
    Android), which needs the MAUI workloads: it builds and tests the agent,
    its web UI, the tray and the tests, as CI's quick build does; the client
    has a CI build of its own (.github/workflows/client.yml).
.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Configuration Release
.EXAMPLE
    .\build.ps1 -Configuration Release -NoClient
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoTest,
    [switch]$NoClient
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

# The whole solution; without the client, the projects that are everything
# else: the tests (and through them the agent, its web UI and the dashboard),
# and the tray with its toasts.
$Targets = if ($NoClient) {
    @("tests\WslcAgent.Server.Tests\WslcAgent.Server.Tests.csproj", "src\WslcAgent.Tray\WslcAgent.Tray.csproj") |
        ForEach-Object { Join-Path $RepoRoot $_ }
} else {
    @(Join-Path $RepoRoot "WslcAgent.slnx")
}
$Scope = if ($NoClient) { @() } else { @(Get-WslcAgentSolutionScope) }

foreach ($target in $Targets) {
    $name = Split-Path -Leaf $target

    # Restore with the same configuration as the build: the MAUI Windows target
    # needs the win-x64 runtime pack only in Release (NETSDK1112 otherwise).
    Write-Host "Restoring $name ($Configuration)..." -ForegroundColor Cyan
    dotnet restore $target -nologo -v q -p:Configuration=$Configuration @Scope
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore of $name failed with exit code $LASTEXITCODE" }

    Write-Host "Building $name ($Configuration)..." -ForegroundColor Cyan
    dotnet build $target -c $Configuration --no-restore -nologo -v q @Scope
    if ($LASTEXITCODE -ne 0) { throw "dotnet build of $name failed with exit code $LASTEXITCODE" }
}

if (-not $NoTest) {
    Write-Host "Testing..." -ForegroundColor Cyan
    dotnet test $Targets[0] -c $Configuration --no-build -nologo @Scope
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}

Write-Host "Done." -ForegroundColor Green
