<#
.SYNOPSIS
    Report the private files this checkout has, what each one enables and what
    is missing, without printing any secret.
.DESCRIPTION
    Looks where the build scripts look (private\, the WSLC_AGENT_* variables,
    private\env.psd1, %USERPROFILE%\.wslc-agent\) and checks what can be
    checked offline: the keystore opens with its password and holds its alias,
    the Firebase files are the right kind of JSON, name this app and the same
    Firebase project, and git ignores every private file. Exits 1 when
    something present is broken; a file that is simply absent is not an error.
    docs\developer\private-files.md says how to make each file.
#>
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $RepoRoot "packaging\Packaging.ps1")
Import-WslcAgentPrivateSettings

$Private = Get-WslcAgentPrivateFolder
$script:WslcAgentCheckBroken = 0
# Where each file is explained and made, section by section.
$PrivateGuide = "docs\developer\private-files.md"


function Read-JsonFile([string]$Path) {
    try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json } catch { return $null }
}

function Get-AppId {
    $csproj = Join-Path $RepoRoot "src\WslcAgent.App\WslcAgent.App.csproj"
    $match = Select-String -LiteralPath $csproj -Pattern '<ApplicationId>([^<]+)</ApplicationId>' | Select-Object -First 1
    if ($match) { return $match.Matches[0].Groups[1].Value }
    return $null
}

Write-Host "Private files of $RepoRoot" -ForegroundColor Cyan
if (Test-Path -LiteralPath (Join-Path $Private "env.psd1")) {
    Write-Host "  private\env.psd1 loaded."
}
Write-Host ""

# --- Android signing key ---------------------------------------------------
Write-Host "Android signing key" -ForegroundColor Cyan
$keystore = $env:WSLC_AGENT_KEYSTORE
$source = "WSLC_AGENT_KEYSTORE"
if (-not $keystore) {
    $keystore = Join-Path $Private "android.keystore"
    $source = "private\android.keystore"
    $perUser = Join-Path $env:USERPROFILE ".wslc-agent\android.keystore"
    if (-not (Test-Path -LiteralPath $keystore) -and (Test-Path -LiteralPath $perUser)) {
        $keystore = $perUser
        $source = "%USERPROFILE%\.wslc-agent\android.keystore"
    }
}
if (-not (Test-Path -LiteralPath $keystore)) {
    if ($env:WSLC_AGENT_KEYSTORE) {
        Write-WslcAgentCheck broken $source "points to a missing file: $keystore" -Guide "$PrivateGuide#1-the-android-signing-key"
    } else {
        Write-WslcAgentCheck absent "android.keystore" "build-client-apk.ps1 generates one in private\ the first time it builds the APK; Debug builds use the SDK's debug key." -Run "& `"$(Join-Path $RepoRoot 'build-client-apk.ps1')`"" -Guide "$PrivateGuide#1-the-android-signing-key"
    }
} else {
    $storePass = $env:WSLC_AGENT_KEYSTORE_PASS
    if (-not $storePass -and (Test-Path -LiteralPath "$keystore.pass")) {
        $storePass = ([System.IO.File]::ReadAllText("$keystore.pass")).Trim()
    }
    $alias = if ($env:WSLC_AGENT_KEY_ALIAS) { $env:WSLC_AGENT_KEY_ALIAS } else { "wslc-agent" }
    if (-not $storePass) {
        Write-WslcAgentCheck broken $source "has no password: add $keystore.pass or set WSLC_AGENT_KEYSTORE_PASS." -Guide "$PrivateGuide#1-the-android-signing-key"
    } else {
        $keytool = try { Find-WslcAgentKeytool } catch { $null }
        if (-not $keytool) {
            Write-WslcAgentCheck ok $source "present; keytool not found, so its password and alias were not checked."
        } else {
            Invoke-WslcAgentNative $keytool @("-list", "-keystore", $keystore, "-storepass", $storePass, "-alias", $alias) | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-WslcAgentCheck ok $source "opens with its password and holds the alias '$alias': APKs are signed with it."
            } else {
                Write-WslcAgentCheck broken $source "does not open with its password, or has no alias '$alias'." -Guide "$PrivateGuide#1-the-android-signing-key"
            }
        }
    }
}
Write-Host ""

# --- Firebase ----------------------------------------------------------------
Write-Host "Push notifications (Firebase)" -ForegroundColor Cyan
$appId = Get-AppId
$clientProject = $null
$googleServices = if ($env:WSLC_AGENT_GOOGLE_SERVICES) { $env:WSLC_AGENT_GOOGLE_SERVICES } else { Join-Path $Private "google-services.json" }
if (-not (Test-Path -LiteralPath $googleServices)) {
    if ($env:WSLC_AGENT_GOOGLE_SERVICES) { Write-WslcAgentCheck broken "WSLC_AGENT_GOOGLE_SERVICES" "points to a missing file: $googleServices" -Guide "$PrivateGuide#2-the-firebase-project-and-google-servicesjson" }
    else { Write-WslcAgentCheck absent "google-services.json" "the Android client builds and receives no push." -Guide "$PrivateGuide#2-the-firebase-project-and-google-servicesjson" }
} else {
    $json = Read-JsonFile $googleServices
    $packages = @($json.client | ForEach-Object { $_.client_info.android_client_info.package_name })
    if (-not $json -or -not $json.project_info.project_id) {
        Write-WslcAgentCheck broken "google-services.json" "is not the file Firebase's console downloads for an Android app." -Guide "$PrivateGuide#2-the-firebase-project-and-google-servicesjson"
    } elseif ($appId -and $packages -notcontains $appId) {
        Write-WslcAgentCheck broken "google-services.json" "registers $($packages -join ', '), not this app's id $appId (WslcAgent.App.csproj)." -Guide "$PrivateGuide#2-the-firebase-project-and-google-servicesjson"
    } else {
        $clientProject = $json.project_info.project_id
        Write-WslcAgentCheck ok "google-services.json" "project ${clientProject}, app ${appId}: the Android client registers for push."
    }
}

$pushKey = if ($env:WSLC_AGENT_PUSH_KEY) { $env:WSLC_AGENT_PUSH_KEY } else { Join-Path $Private "firebase-service-account.json" }
if (-not (Test-Path -LiteralPath $pushKey)) {
    if ($env:WSLC_AGENT_PUSH_KEY) { Write-WslcAgentCheck broken "WSLC_AGENT_PUSH_KEY" "points to a missing file: $pushKey" -Guide "$PrivateGuide#3-the-service-account-key" }
    else { Write-WslcAgentCheck absent "firebase-service-account.json" "the agent installer carries no key; the agent sends no push." -Guide "$PrivateGuide#3-the-service-account-key" }
} else {
    $json = Read-JsonFile $pushKey
    if (-not $json -or $json.type -ne "service_account" -or -not $json.private_key -or -not $json.client_email) {
        Write-WslcAgentCheck broken "firebase-service-account.json" "is not a service account key (Firebase console > Project settings > Service accounts)." -Guide "$PrivateGuide#3-the-service-account-key"
    } elseif ($clientProject -and $json.project_id -ne $clientProject) {
        Write-WslcAgentCheck broken "firebase-service-account.json" "belongs to project $($json.project_id), the client to ${clientProject}: pushes would never arrive." -Guide "$PrivateGuide#3-the-service-account-key"
    } else {
        Write-WslcAgentCheck ok "firebase-service-account.json" "project $($json.project_id): the agent installer carries it and the agent pushes."
    }
}
Write-Host ""

# --- Git ---------------------------------------------------------------------
Write-Host "Git" -ForegroundColor Cyan
$tracked = @(git -C $RepoRoot ls-files -- private | Where-Object { $_ -notin @("private/README.md", "private/env.example.psd1") })
if ($tracked.Count -gt 0) {
    $quoted = ($tracked | ForEach-Object { '"' + $_ + '"' }) -join ' '
    Write-WslcAgentCheck broken "private\" "git tracks $($tracked -join ', '): take it out of git, and replace the secret it held." -Run "git -C `"$RepoRoot`" rm --cached -- $quoted" -Guide "$PrivateGuide#where-they-go"
} else {
    Write-WslcAgentCheck ok "private\" "git tracks nothing in it but README.md and env.example.psd1."
}
$files = @(Get-ChildItem -LiteralPath $Private -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notin @("README.md", "env.example.psd1") })
foreach ($file in $files) {
    git -C $RepoRoot check-ignore -q -- "private/$($file.Name)"
    if ($LASTEXITCODE -ne 0) { Write-WslcAgentCheck broken "private\$($file.Name)" "is not ignored by git: check .gitignore." -Guide "$PrivateGuide#where-they-go" }
}
Write-Host ""

if ($script:WslcAgentCheckBroken -gt 0) {
    Write-Host "$($script:WslcAgentCheckBroken) problem(s) above. docs\developer\private-files.md says how to make each file." -ForegroundColor Red
    exit 1
}
Write-Host "Nothing broken. What is absent only switches off what it enables." -ForegroundColor Green
