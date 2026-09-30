<#
.SYNOPSIS
    Build the per-user Windows installer of the agent: dist\wslc-ai-agent.msi.
.DESCRIPTION
    Bumps the agent version (Directory.Build.props, patch), publishes
    WslcAgent.Server self-contained for win-x64 with the Blazor UI inside,
    adds LICENSE and the third-party notices, and wraps the payload with WiX.
    The MSI installs under %LOCALAPPDATA%\WSLC-AI-Agent, asks for the bind
    host and port in its wizard (WSLC_BINDHOST / WSLC_AGENTPORT properties,
    defaults 127.0.0.1 / 8069), stores them under HKCU, registers the logon
    task that starts the agent now and at every logon, and adds a Start Menu
    shortcut to the dashboard URL.
    The wizard also asks for the package folder, where the agent finds its own
    newer installer and the clients' (WSLC_PACKAGESFOLDER, written to
    wslc-ai-agent.ini beside the agent). It offers this checkout's dist, where
    the build scripts put the installers, so an agent built here updates
    itself from what is built here; -Release offers C:\Berpiztu\wslc-ai-agent,
    the folder of the published releases.
    -NoBump rebuilds the current version; -Clean deletes previous outputs.
#>
param(
    [switch]$NoBump,
    [switch]$Clean,
    [switch]$Release
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")
Import-WslcAgentPrivateSettings

$Project = Join-Path $RepoRoot "src\WslcAgent.Server\WslcAgent.Server.csproj"
$WixProj = Join-Path $RepoRoot "packaging\agent-install\WslcAgent.Agent.wixproj"
$Stage = Join-Path $RepoRoot "dist\agent-msi-payload"
$MsiDest = Join-Path $RepoRoot "dist\wslc-ai-agent.msi"

if ($Clean) {
    foreach ($path in @($Stage, $MsiDest, (Join-Path $RepoRoot "packaging\agent-install\bin"), (Join-Path $RepoRoot "packaging\agent-install\obj"))) {
        if (Test-Path -LiteralPath $path) { Remove-Item -Recurse -Force -LiteralPath $path }
    }
}

$version = Update-WslcAgentVersion -NoBump:$NoBump

if (Test-Path -LiteralPath $Stage) { Remove-Item -Recurse -Force -LiteralPath $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

Write-Host "Publishing WSLC AI Agent $version (self-contained win-x64)..." -ForegroundColor Cyan
& dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -nologo -v q -o $Stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $Stage "wslc-ai-agent.exe"))) { throw "Publish produced no wslc-ai-agent.exe in $Stage" }

# The agent's icon beside the clock and its window, a program of its own in tray\, self-contained as the agent is: the
# Windows desktop runtime it needs is not the agent's.
$Tray = Join-Path $RepoRoot "src\WslcAgent.Tray\WslcAgent.Tray.csproj"
Write-Host "Publishing the agent's tray icon (self-contained win-x64)..." -ForegroundColor Cyan
& dotnet publish $Tray -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -nologo -v q -o (Join-Path $Stage "tray")
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the tray icon failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $Stage "tray\wslc-ai-agent-tray.exe"))) { throw "Publish produced no tray\wslc-ai-agent-tray.exe in $Stage" }

# The Firebase key the agent pushes notifications to phones with, into the
# agent's data folder (data\ under the install folder): WSLC_AGENT_PUSH_KEY,
# else private\firebase-service-account.json. Without it the agent pushes
# nothing; the key can still be copied into the data folder by hand.
# Never into a release: the key is a secret, and a published installer hands
# it to whoever downloads it, who could then push through your Firebase project.
$PushKey = if ($env:WSLC_AGENT_PUSH_KEY) { $env:WSLC_AGENT_PUSH_KEY } else { Join-Path (Get-WslcAgentPrivateFolder) "firebase-service-account.json" }
if ($Release) {
    Write-Host "A release carries no Firebase key: its agent pushes no notifications until one is copied into its data folder." -ForegroundColor Yellow
} elseif (Test-Path -LiteralPath $PushKey) {
    New-Item -ItemType Directory -Force -Path (Join-Path $Stage "data") | Out-Null
    Copy-Item -LiteralPath $PushKey -Destination (Join-Path $Stage "data\firebase-service-account.json")
} elseif ($env:WSLC_AGENT_PUSH_KEY) {
    throw "WSLC_AGENT_PUSH_KEY points to a missing file: $PushKey"
} else {
    Write-Host "No private\firebase-service-account.json: the agent will push no notifications to phones (docs/developer/private-files.md)." -ForegroundColor Yellow
}

Remove-WslcAgentDebugFiles -Path $Stage
# The WebAssembly.Server package publishes its browser debugging proxy; an
# installed agent has no use for it.
$debugProxy = Join-Path $Stage "BlazorDebugProxy"
if (Test-Path -LiteralPath $debugProxy) { Remove-Item -Recurse -Force -LiteralPath $debugProxy }
Copy-WslcAgentNotices -Destination $Stage

Write-WslcAgentWixFileList -Stage $Stage -OutFile (Join-Path $RepoRoot "packaging\agent-install\GeneratedFiles.wxs")

$msiVersion = ConvertTo-WixProductVersion $version
Write-Host "Building wslc-ai-agent.msi $msiVersion (WiX)..." -ForegroundColor Cyan
# The package folder the wizard offers: where this checkout puts the
# installers, or the releases' fixed one.
$PackagesFolder = if ($Release) { "C:\Berpiztu\wslc-ai-agent" } else { Join-Path $RepoRoot "dist" }
Write-Host "Package folder offered by the wizard: $PackagesFolder" -ForegroundColor DarkGray
$built = Build-WslcAgentMsi -WixProj $WixProj -Version $msiVersion -Properties @{ PackagesFolder = $PackagesFolder }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $MsiDest) | Out-Null
Copy-Item -LiteralPath $built -Destination $MsiDest -Force
Set-WslcAgentMsiExplorerVersion -Path $MsiDest -Name "WSLC AI Agent" -Version $msiVersion

Write-Host ""
Write-Host "WSLC AI Agent MSI ready: $MsiDest (version $msiVersion)" -ForegroundColor Green
Write-Host "Install (per-user): msiexec /i `"$MsiDest`" [WSLC_BINDHOST=127.0.0.1] [WSLC_AGENTPORT=8069] [WSLC_PACKAGESFOLDER=`"$PackagesFolder`"]"
