<#
.SYNOPSIS
    Publish dist\wslc-ai-client.apk with every Android ABI.
.DESCRIPTION
    Wrapper around build-client-apk.ps1 -Full (arm64-v8a, armeabi-v7a, x86,
    x86_64) for emulators and unusual devices. Day-to-day phone builds use
    build-client-apk.ps1, which is arm64-v8a only and much smaller.
#>
param(
    [switch]$NoBump,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"
& (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "build-client-apk.ps1") -Full -NoBump:$NoBump -Clean:$Clean
exit $LASTEXITCODE
