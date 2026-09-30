<#
.SYNOPSIS
    Build and test wslc-agent, the same steps CI runs.
.DESCRIPTION
    Restores, builds WslcAgent.slnx and runs the tests. Works from any
    current directory. Use -NoTest to build only and -Configuration Release
    to match CI exactly.
.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Configuration Release
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoTest
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
$Solution = Join-Path $RepoRoot "WslcAgent.slnx"
. (Join-Path $RepoRoot "packaging\Packaging.ps1")
$Scope = @(Get-WslcAgentSolutionScope)

# Restore with the same configuration as the build: the MAUI Windows target
# needs the win-x64 runtime pack only in Release (NETSDK1112 otherwise).
Write-Host "Restoring ($Configuration)..." -ForegroundColor Cyan
dotnet restore $Solution -nologo -v q -p:Configuration=$Configuration @Scope
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Host "Building ($Configuration)..." -ForegroundColor Cyan
dotnet build $Solution -c $Configuration --no-restore -nologo -v q @Scope
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

if (-not $NoTest) {
    Write-Host "Testing..." -ForegroundColor Cyan
    dotnet test $Solution -c $Configuration --no-build -nologo @Scope
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }
}

Write-Host "Done." -ForegroundColor Green
