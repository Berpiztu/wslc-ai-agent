<#
.SYNOPSIS
    Publish the Android client to dist\wslc-ai-client.apk.
.DESCRIPTION
    Bumps the client version in src\WslcAgent.App (versionName and versionCode),
    publishes a signed Release APK and copies it to dist\. Default is arm64-v8a
    only (phones); -Full bundles every ABI for emulators and unusual devices.
    The signing key comes from WSLC_AGENT_KEYSTORE, private\android.keystore
    or %USERPROFILE%\.wslc-agent\android.keystore, and is generated in
    private\ when missing; see docs\developer\private-files.md.
    -NoBump rebuilds the current version.
#>
[CmdletBinding()]
param(
    [switch]$NoBump,
    [switch]$Clean,
    [switch]$Full,
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
$Tfm = "net10.0-android"
$ApkDest = Join-Path $RepoRoot "dist\wslc-ai-client.apk"

if ($Clean -and (Test-Path -LiteralPath $ApkDest)) { Remove-Item -Force -LiteralPath $ApkDest }

$signing = Resolve-WslcAgentAndroidSigning
$client = Update-WslcAgentClientVersion -Csproj $Csproj -NoBump:$NoBump
Clear-WslcAgentIconCache
New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot "dist") | Out-Null

$publishArgs = @(
    $Csproj, "-c", "Release", "-f", $Tfm, "-nologo") + @(Get-WslcAgentDotnetOutput) + @(
    "-p:AndroidPackageFormat=apk",
    "-p:AndroidKeyStore=true",
    "-p:AndroidSigningKeyStore=$($signing.Keystore)",
    "-p:AndroidSigningStorePass=$($signing.StorePass)",
    "-p:AndroidSigningKeyAlias=$($signing.Alias)",
    "-p:AndroidSigningKeyPass=$($signing.KeyPass)"
)
if ($Full) {
    $publishArgs += "-p:AndroidCreatePackagePerAbi=false"
    $abiLabel = "all ABIs"
} else {
    $publishArgs += @("-r", "android-arm64")
    $abiLabel = "arm64-v8a"
}

Write-Host "Publishing WSLC AI Client $($client.Display) for Android (Release APK, $abiLabel)..." -ForegroundColor Cyan
# The command as it runs, the signing passwords hidden: they are secrets.
Write-Verbose ("dotnet publish " + (($publishArgs | ForEach-Object { "$_" -replace '^(-p:AndroidSigning(StorePass|KeyPass))=.*$', '$1=***' }) -join " "))
& dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$searchRoot = Join-Path $RepoRoot "src\WslcAgent.App\bin\Release\$Tfm"
$apk = Get-ChildItem -LiteralPath $searchRoot -Recurse -Filter "*-Signed.apk" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw "Publish succeeded but no signed APK was found under $searchRoot" }

Copy-Item -LiteralPath $apk.FullName -Destination $ApkDest -Force
Write-Host ""
Write-Host "WSLC AI Client APK ready: $ApkDest ($([math]::Round($apk.Length / 1MB, 1)) MB, $abiLabel)" -ForegroundColor Green
Write-Host "versionName $($client.Display) / versionCode $($client.Build); signed with $($signing.Keystore)"
