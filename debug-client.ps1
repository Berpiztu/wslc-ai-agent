<#
.SYNOPSIS
    Build the Windows client in Debug and launch it.
.DESCRIPTION
    Builds src\WslcAgent.App for net10.0-windows10.0.19041.0 (Debug unless
    -Configuration Release) and starts wslc-ai-client.exe from the build
    output. The client talks to the agent at -AgentUrl (default
    http://127.0.0.1:8070/, stored in the app's preferences); start the agent
    first with .\start-agent.ps1. Works from any current directory.
.EXAMPLE
    .\debug-client.ps1
.EXAMPLE
    .\debug-client.ps1 -NoBuild -AgentUrl http://192.168.1.20:8070/
#>
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$AgentUrl = "http://127.0.0.1:8070/",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot

$Csproj = Join-Path $RepoRoot "src\WslcAgent.App\WslcAgent.App.csproj"
$Tfm = "net10.0-windows10.0.19041.0"

if (-not $NoBuild) {
    # MAUI's icon generator caches under obj\**\resizetizer and misses metadata changes.
    Get-ChildItem -Path (Join-Path $RepoRoot "src\WslcAgent.App\obj") -Directory -Recurse -Filter resizetizer -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -Recurse -Force -LiteralPath $_.FullName }
    Write-Host "Building WSLC AI Client for Windows ($Configuration)..." -ForegroundColor Cyan
    dotnet build $Csproj -c $Configuration -f $Tfm -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

$exe = Get-ChildItem -Path (Join-Path $RepoRoot "src\WslcAgent.App\bin\$Configuration\$Tfm") -Recurse -Filter "wslc-ai-client.exe" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $exe) { throw "wslc-ai-client.exe not found under src\WslcAgent.App\bin\$Configuration\$Tfm; build first." }

try {
    $health = Invoke-WebRequest -Uri ($AgentUrl.TrimEnd('/') + "/api/v1/health") -UseBasicParsing -TimeoutSec 3
    Write-Host "Agent at $AgentUrl answers: $($health.Content)" -ForegroundColor DarkGray
} catch {
    Write-Host "No agent answers at $AgentUrl. Start one with .\start-agent.ps1; the client will show an error until then." -ForegroundColor Yellow
}

# The app reads its agent URL from preferences; a command-line argument seeds it
# so a debug session can point at any agent without touching settings.
Write-Host "Starting $($exe.FullName)" -ForegroundColor Green
Start-Process -FilePath $exe.FullName -ArgumentList @("--agent-url", $AgentUrl) -WorkingDirectory $exe.DirectoryName | Out-Null
