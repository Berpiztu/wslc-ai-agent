<#
.SYNOPSIS
    Build the Android client in Debug, deploy it to an emulator or device and launch it.
.DESCRIPTION
    Uses `dotnet build -t:Install`, which is the only correct way to deploy a
    Debug build: Debug APKs fast-deploy their assemblies separately, so a hand
    `adb install` of the APK would run stale code. When no device is attached
    it starts an emulator: -Avd names it, otherwise you pick one from the list
    of installed AVDs. It waits for boot, installs, then launches the app
    pointed at -AgentUrl. From the
    emulator the host machine is 10.0.2.2, so the default agent URL is
    http://10.0.2.2:8070/; pass your PC's LAN address for a physical phone,
    or http://127.0.0.1:<port>/ for one on the USB cable: an agent URL on the
    device's own loopback has that port reversed to the PC first
    (adb reverse), so the device reaches the PC's agent as local.
    debug-phone.ps1 does that for the phone on the cable.
.EXAMPLE
    .\debug-android.ps1
.EXAMPLE
    .\debug-android.ps1 -Avd pixel_7_-_api_35 -AgentUrl http://10.0.2.2:8070/
#>
param(
    [string]$Avd,
    [string]$Device,
    # The physical phone attached over adb, whatever its serial: the one
    # device that is not an emulator (debug-phone.ps1).
    [switch]$Phone,
    [string]$AgentUrl = "http://10.0.2.2:8070/",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

$Csproj = Join-Path $RepoRoot "src\WslcAgent.App\WslcAgent.App.csproj"
$Tfm = "net10.0-android"
$PackageId = "ai.berpiztu.wslcagent"
$Activity = "$PackageId/$PackageId.MainActivity"

$sdk = Find-WslcAgentAndroidSdk
if (-not $sdk) { throw "Android SDK not found. Set ANDROID_HOME to the folder that contains platform-tools\adb.exe (.\check-prereqs.ps1)." }
$adb = Join-Path $sdk "platform-tools\adb.exe"
$emulator = Join-Path $sdk "emulator\emulator.exe"

function Get-AttachedDevices {
    & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "^\S+\s+device$" } | ForEach-Object { ($_ -split "\s+")[0] }
}

$devices = @(Get-AttachedDevices)
if ($Phone -and -not $Device) {
    $phones = @($devices | Where-Object { $_ -notlike "emulator-*" })
    if ($phones.Count -eq 0) { throw "No phone attached. Plug it in with USB debugging on and accept the prompt on the phone." }
    if ($phones.Count -gt 1) { throw "More than one phone attached ($($phones -join ', ')): name one with -Device." }
    $Device = $phones[0]
}

if ($Device) {
    if ($devices -notcontains $Device) { throw "Device '$Device' is not attached. adb devices lists: $($devices -join ', ')" }
} elseif ($devices.Count -eq 0) {
    if (-not $Avd) {
        $avds = @(& $emulator -list-avds | Where-Object { $_ -and $_.Trim() -ne "" } | ForEach-Object { $_.Trim() })
        if ($avds.Count -eq 0) { throw "No device attached and no AVD defined. Create one in Android Studio or plug in a phone." }
        if ($avds.Count -eq 1) {
            $Avd = $avds[0]
        } else {
            Write-Host "No device attached. Available emulators:" -ForegroundColor Cyan
            for ($i = 0; $i -lt $avds.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $avds[$i]) }
            do {
                $answer = Read-Host "Start which one? [1-$($avds.Count)]"
                $index = 0
                $valid = [int]::TryParse($answer, [ref]$index) -and $index -ge 1 -and $index -le $avds.Count
            } until ($valid)
            $Avd = $avds[$index - 1]
        }
    }
    Write-Host "Starting emulator '$Avd'..." -ForegroundColor Cyan
    Start-Process -FilePath $emulator -ArgumentList @("-avd", $Avd, "-no-boot-anim") -WindowStyle Minimized | Out-Null
    & $adb wait-for-device | Out-Null
    for ($i = 0; $i -lt 120; $i++) {
        $booted = (Invoke-WslcAgentNative $adb @("shell", "getprop", "sys.boot_completed")) -join ""
        if ($booted.Trim() -eq "1") { break }
        Start-Sleep -Seconds 2
    }
    if ($booted.Trim() -ne "1") { throw "The emulator did not finish booting." }
    $devices = @(Get-AttachedDevices)
}
$target = if ($Device) { $Device } else { $devices[0] }
$env:ANDROID_SERIAL = $target
Write-Host "Target device: $target" -ForegroundColor DarkGray

# The agent on the device's own loopback is the PC's, carried by the cable.
$agent = [Uri]$AgentUrl
if ($agent.IsLoopback) {
    & $adb -s $target reverse "tcp:$($agent.Port)" "tcp:$($agent.Port)" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "adb reverse tcp:$($agent.Port) failed on $target" }
    Write-Host "Port $($agent.Port) of $target reversed to this PC" -ForegroundColor DarkGray
}

if (-not $NoBuild) {
    # MAUI's icon generator caches under obj\**\resizetizer and misses metadata changes.
    Get-ChildItem -Path (Join-Path $RepoRoot "src\WslcAgent.App\obj") -Directory -Recurse -Filter resizetizer -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -Recurse -Force -LiteralPath $_.FullName }
    Write-Host "Building and deploying WSLC AI Client for Android (Debug, fast deployment)..." -ForegroundColor Cyan
    dotnet build $Csproj -c Debug -f $Tfm -t:Install -nologo -v q -p:AdbTarget="-s $target"
    if ($LASTEXITCODE -ne 0) { throw "dotnet build -t:Install failed with exit code $LASTEXITCODE" }
}

Write-Host "Launching $PackageId with agent $AgentUrl" -ForegroundColor Green
& $adb -s $target shell am force-stop $PackageId | Out-Null
& $adb -s $target shell am start -n $Activity --es agent_url "$AgentUrl" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "adb failed to start $Activity" }
Write-Host "Logs: adb -s $target logcat -s DOTNET WslcAgent" -ForegroundColor DarkGray
