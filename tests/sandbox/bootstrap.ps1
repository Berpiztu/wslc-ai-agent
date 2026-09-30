<#
.SYNOPSIS
    Runs inside Windows Sandbox at logon (start-sandbox.ps1): turns the
    Sandbox into the Windows 11 a new user has, then opens a terminal for the
    Quick start.
.DESCRIPTION
    Windows Sandbox is a bare Windows: it lacks winget, which every Windows 11
    has, and which the prerequisites are installed with. This installs winget
    the way Microsoft documents for the Sandbox, and nothing else: no .NET, no
    git, no execution policy. Those are the user's steps, and the test is that
    the README gets them there.
#>
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$Setup = Split-Path -Parent $MyInvocation.MyCommand.Path
Start-Transcript -Path (Join-Path $env:USERPROFILE "Desktop\sandbox-setup.log") | Out-Null

try {
    Write-Host "Installing winget, as every Windows 11 has it..." -ForegroundColor Cyan
    Install-PackageProvider -Name NuGet -Force | Out-Null
    Install-Module -Name Microsoft.WinGet.Client -Force -Repository PSGallery | Out-Null
    Repair-WinGetPackageManager -AllUsers
    Write-Host "winget ready." -ForegroundColor Green
} catch {
    Write-Host "winget could not be installed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "The Quick start still works for everything that does not need winget." -ForegroundColor Yellow
}
Stop-Transcript | Out-Null

# The terminal the tester works in: Windows PowerShell with the policy of a
# fresh machine, so step 1 of the Quick start is really needed.
$cloneUrl = (Get-Content -LiteralPath (Join-Path $Setup "clone-url.txt") -Raw).Trim()
$welcome = @"
Write-Host 'Clean Windows ready. Follow the README Quick start from step 1.' -ForegroundColor Green
Write-Host 'Clone this repository with: git clone $cloneUrl' -ForegroundColor Cyan
Write-Host 'Everything here is erased when the Sandbox closes.' -ForegroundColor DarkGray
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($welcome))
Start-Process powershell.exe -WorkingDirectory $env:USERPROFILE -ArgumentList "-NoExit", "-NoProfile", "-EncodedCommand", $encoded
