<#
.SYNOPSIS
    Build the Android client in Debug and deploy it to the phone on the USB cable.
.DESCRIPTION
    debug-android.ps1 aimed at the physical phone attached over adb (the one
    device that is not an emulator), built for its own architecture, and
    launched against the PC's agent through the cable: the agent URL is the
    phone's own loopback, whose port debug-android.ps1 reverses to the PC.
    With more than one phone attached, -Device names it.
.EXAMPLE
    .\debug-phone.ps1
.EXAMPLE
    .\debug-phone.ps1 -AgentUrl http://127.0.0.1:8069/ -NoBuild
#>
[CmdletBinding()]
param(
    [string]$Device,
    [string]$AgentUrl = "http://127.0.0.1:8070/",
    [switch]$NoBuild,
    [switch]$Quiet
)

# Verbose unless asked not to: every command and dotnet's whole log are what
# the owner reads a build by (Get-WslcAgentDotnetOutput follows this).
if (-not $Quiet) { $VerbosePreference = "Continue" }

$ErrorActionPreference = "Stop"
& (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "debug-android.ps1") -Phone -Device $Device -AgentUrl $AgentUrl -NoBuild:$NoBuild -Quiet:$Quiet
