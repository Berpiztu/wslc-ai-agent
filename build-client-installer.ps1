<#
.SYNOPSIS
    Build the per-user Windows installer of the client: dist\wslc-ai-client.msi.
.DESCRIPTION
    Bumps the client version in src\WslcAgent.App (ApplicationDisplayVersion,
    ApplicationVersion), publishes the unpackaged self-contained MAUI Windows
    app, adds LICENSE and the third-party notices, and wraps the payload with
    WiX. The MSI installs under %LOCALAPPDATA%\WSLC-AI-Client, stores the
    agent URL under HKCU (WSLC_AGENTURL property, default
    http://127.0.0.1:8069/) and adds Desktop and Start Menu shortcuts.
    -NoBump rebuilds the current version; -Clean deletes previous outputs.
#>
[CmdletBinding()]
param(
    [switch]$NoBump,
    [switch]$Clean,
    [switch]$Quiet
)

# Verbose unless asked not to: every command and dotnet's whole log are what
# the owner reads a build by (Get-WslcAgentDotnetOutput follows this).
if (-not $Quiet) { $VerbosePreference = "Continue" }

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")
Import-WslcAgentPrivateSettings

$Csproj = Join-Path $RepoRoot "src\WslcAgent.App\WslcAgent.App.csproj"
$WixProj = Join-Path $RepoRoot "packaging\client-install\WslcAgent.Client.wixproj"
$Stage = Join-Path $RepoRoot "dist\client-msi-payload"
$MsiDest = Join-Path $RepoRoot "dist\wslc-ai-client.msi"
$Tfm = "net10.0-windows10.0.19041.0"

if ($Clean) {
    foreach ($path in @($Stage, $MsiDest, (Join-Path $RepoRoot "packaging\client-install\bin"), (Join-Path $RepoRoot "packaging\client-install\obj"))) {
        if (Test-Path -LiteralPath $path) { Remove-Item -Recurse -Force -LiteralPath $path }
    }
}

$client = Update-WslcAgentClientVersion -Csproj $Csproj -NoBump:$NoBump
Clear-WslcAgentIconCache

if (Test-Path -LiteralPath $Stage) { Remove-Item -Recurse -Force -LiteralPath $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null

# WslcAgentWindowsOnly keeps restore away from the Android target (see the csproj).
Write-Host "Publishing WSLC AI Client $($client.Display) (unpackaged self-contained win-x64)..." -ForegroundColor Cyan
$Output = @(Get-WslcAgentDotnetOutput)
Write-Verbose "dotnet publish $Csproj -c Release -f $Tfm -r win-x64 --self-contained true -p:WslcAgentWindowsOnly=true -p:WindowsPackageType=None -p:PublishSingleFile=false $Output -o $Stage"
& dotnet publish $Csproj -c Release -f $Tfm -r win-x64 --self-contained true `
    -p:WslcAgentWindowsOnly=true -p:WindowsPackageType=None -p:PublishSingleFile=false -nologo @Output -o $Stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
if (-not (Test-Path -LiteralPath (Join-Path $Stage "wslc-ai-client.exe"))) { throw "Publish produced no wslc-ai-client.exe in $Stage" }

Remove-WslcAgentDebugFiles -Path $Stage
Copy-WslcAgentNotices -Destination $Stage

Write-WslcAgentWixFileList -Stage $Stage -OutFile (Join-Path $RepoRoot "packaging\client-install\GeneratedFiles.wxs")

$msiVersion = ConvertTo-WixProductVersion $client.Display
Write-Host "Building wslc-ai-client.msi $msiVersion (WiX)..." -ForegroundColor Cyan
# The question asked after an uninstall, whether to remove the preferences
# too: RemovePreferences.ps1, encoded as PowerShell takes it, so the installer
# carries it with no quoting to get wrong.
$removePreferences = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
    [IO.File]::ReadAllText((Join-Path $RepoRoot "packaging\client-install\RemovePreferences.ps1"))))
$built = Build-WslcAgentMsi -WixProj $WixProj -Version $msiVersion -Properties @{ RemovePreferencesCommand = $removePreferences }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $MsiDest) | Out-Null
Copy-Item -LiteralPath $built -Destination $MsiDest -Force
Set-WslcAgentMsiExplorerVersion -Path $MsiDest -Name "WSLC AI Client" -Version $msiVersion

Write-Host ""
Write-Host "WSLC AI Client MSI ready: $MsiDest (version $msiVersion, build $($client.Build))" -ForegroundColor Green
Write-Host "Install (per-user): msiexec /i `"$MsiDest`" [WSLC_AGENTURL=http://host:8069/]"
