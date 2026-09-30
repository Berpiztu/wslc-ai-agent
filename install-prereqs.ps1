<#
.SYNOPSIS
    Install everything check-prereqs.ps1 finds missing, in one go.
.DESCRIPTION
    Asks Windows once for administrator rights (the .NET SDK, its workloads,
    WSL and the Virtual Machine Platform install for the whole machine) and
    goes on in an administrator window of its own, which stays open to read.
    There it runs, in order, the command check-prereqs.ps1 prints under each
    missing piece: the .NET SDK before its workloads, the JDK and the workloads
    before the Android SDK. Before each command it reloads the PATH from
    Windows, so what one installed is seen by the next without a new terminal.
    Then it checks again and runs what is still missing, until nothing is left
    or nothing new can be tried; a command that failed is not run twice.
#>
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Check = Join-Path $RepoRoot "check-prereqs.ps1"

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Opening an administrator PowerShell to install the prerequisites; Windows asks once." -ForegroundColor Cyan
    $shell = (Get-Process -Id $PID).Path
    try {
        Start-Process -FilePath $shell -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-File", "`"$PSCommandPath`"")
    } catch {
        throw "Administrator rights were refused, and nothing was installed: $($_.Exception.Message)"
    }
    Write-Host "It goes on in that window. When it ends, CLOSE this terminal and VS Code and open a new one: this one does not see what it installed." -ForegroundColor Yellow
    return
}

# What the installers added to the PATH, in this window: it started before them.
function Update-SessionPath {
    $env:Path = @([Environment]::GetEnvironmentVariable("Path", "Machine"), [Environment]::GetEnvironmentVariable("Path", "User")) -join ";"
}

$tried = @{}
$restart = $false
# Three passes of installs at most: the first installs what it can; a later
# one what only became checkable after it. Every pass starts with the check,
# so the last one shows where the machine is.
for ($pass = 1; $pass -le 4; $pass++) {
    Update-SessionPath
    $fixes = @(& $Check -PassThru | Where-Object { $_ -and $_.PSObject.Properties["Run"] })
    $pending = @($fixes | Where-Object { -not $tried.ContainsKey($_.Run -join "`n") })
    if ($pending.Count -eq 0 -or $pass -eq 4) { break }

    foreach ($fix in $pending) {
        $tried[$fix.Run -join "`n"] = $true
        Write-Host ""
        Write-Host "Installing: $($fix.What)" -ForegroundColor Cyan
        foreach ($command in $fix.Run) {
            Update-SessionPath
            Write-Host "  $command" -ForegroundColor DarkGray
            $global:LASTEXITCODE = 0
            try {
                Invoke-Expression $command
                if ($LASTEXITCODE -ne 0) { Write-Host "  ended with exit code $LASTEXITCODE" -ForegroundColor Yellow }
            } catch {
                Write-Host "  failed: $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        if ($fix.Then -like "restart Windows*") { $restart = $true }
    }
    Write-Host ""
    Write-Host "Checking again..." -ForegroundColor Cyan
    Write-Host ""
}

Write-Host ""
if ($restart) {
    Write-Host "RESTART Windows to finish installing WSL, then run .\check-prereqs.ps1 again." -ForegroundColor Yellow
}
Write-Host "CLOSE every terminal and VS Code and open them again: the ones open before this did not see what it installed." -ForegroundColor Yellow
