<#
.SYNOPSIS
    Register, start, stop or remove the logon task that keeps WSLC AI Agent
    running in the user's session.
.DESCRIPTION
    Installed next to wslc-ai-agent.exe by the MSI, which calls -Register
    after installing and -Unregister before uninstalling. wslc only works
    inside an interactive user session, so the agent is neither a Windows
    service nor a session-0 process: a per-user scheduled task launches it at
    logon (and right after installation), the agent reads its bind host and
    port from HKCU\Software\Berpiztu\wslc-agent\Agent.
.PARAMETER Register
    Stop any running agent, (re)create the logon task and start it now.
.PARAMETER Unregister
    Stop the agent and delete the logon task.
.PARAMETER Start
    Start the agent, with no console, unless it already answers on its port,
    and its icon beside the clock unless it is there; this is what the task
    itself runs.
.PARAMETER Stop
    Stop the process listening on the agent's port.
.PARAMETER ScheduleUpdate
    What the agent runs to update itself, from a copy of this script outside
    the install folder: register and start the one-off task
    WSLC-AI-Agent-Update, which runs this script with -Update in the user's
    session. A task and not a child of the agent, which lives inside the logon
    task the installer re-registers.
.PARAMETER Update
    Wait for the agent to stop, install -Msi quietly with the agent's own bind
    host and port, and write how it went to -Result. The installer's last step
    starts the new agent; when the installer fails, the old one is started
    again.
#>
[CmdletBinding(DefaultParameterSetName = "Register")]
param(
    [Parameter(ParameterSetName = "Register")][switch]$Register,
    [Parameter(ParameterSetName = "Unregister")][switch]$Unregister,
    [Parameter(ParameterSetName = "Start")][switch]$Start,
    [Parameter(ParameterSetName = "Stop")][switch]$Stop,
    [Parameter(ParameterSetName = "ScheduleUpdate")][switch]$ScheduleUpdate,
    [Parameter(ParameterSetName = "Update")][switch]$Update,
    [Parameter(ParameterSetName = "ScheduleUpdate", Mandatory)][Parameter(ParameterSetName = "Update", Mandatory)][string]$Msi,
    [Parameter(ParameterSetName = "ScheduleUpdate", Mandatory)][Parameter(ParameterSetName = "Update", Mandatory)][string]$Version,
    [Parameter(ParameterSetName = "ScheduleUpdate", Mandatory)][Parameter(ParameterSetName = "Update", Mandatory)][string]$Result
)

$ErrorActionPreference = "Stop"
$TaskName = "WSLC-AI-Agent"
$UpdateTaskName = "WSLC-AI-Agent-Update"
$RegistryKey = "HKCU:\Software\Berpiztu\wslc-agent\Agent"
$ScriptPath = $PSCommandPath
# An update runs from a copy of this script outside the install folder, which
# the installer replaces: the agent is where the installer said it put it.
$InstallDir = if ($ScheduleUpdate -or $Update) {
    (Get-ItemProperty -Path $RegistryKey -Name InstallDir).InstallDir.TrimEnd("\")
} else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}
$AgentExe = Join-Path $InstallDir "wslc-ai-agent.exe"
# The agent's icon beside the clock, which opens its window: the agent itself runs with no console.
$TrayExe = Join-Path $InstallDir "tray\wslc-ai-agent-tray.exe"

function Get-AgentPort {
    $value = (Get-ItemProperty -Path $RegistryKey -Name Port -ErrorAction SilentlyContinue).Port
    if ($value -and [int]::TryParse($value, [ref]$null)) { return [int]$value }
    return 8069
}

function Get-ListenerProcessIds {
    $port = Get-AgentPort
    return @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
}

function Test-AgentHealth {
    try {
        $null = Invoke-RestMethod -Uri "http://127.0.0.1:$(Get-AgentPort)/api/v1/health" -TimeoutSec 2
        return $true
    } catch {
        return $false
    }
}

# The icon's processes of this install: its own copy, never one picked by
# image name alone.
function Get-TrayProcessIds {
    return @(Get-Process -Name "wslc-ai-agent-tray" -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and ($_.Path -ieq $TrayExe) } |
        Select-Object -ExpandProperty Id)
}

function Start-Tray {
    if ((Test-Path -LiteralPath $TrayExe) -and -not (Get-TrayProcessIds)) {
        Start-Process -FilePath $TrayExe -WorkingDirectory (Split-Path -Parent $TrayExe) | Out-Null
    }
}

function Start-Agent {
    Start-Tray
    if (Test-AgentHealth) { return }
    if (Get-ListenerProcessIds) { throw "Port $(Get-AgentPort) is in use by another process; WSLC AI Agent cannot start." }
    Start-Process -FilePath $AgentExe -WorkingDirectory $InstallDir -WindowStyle Hidden | Out-Null
}

function Stop-Agent {
    # Only the process bound to the agent's own port is ever stopped; never a
    # process picked by image name, the user may run another agent by hand.
    foreach ($processId in Get-ListenerProcessIds) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
    # The icon goes with it, so an update or an uninstall finds its files free.
    foreach ($processId in Get-TrayProcessIds) {
        Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 400
}

function Remove-LogonTask {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }
}

function Get-AgentPackagesFolder {
    # The folder the installer asked for, from the plain-text file it wrote
    # beside the agent: an update keeps it. Settings may override it, but that
    # choice lives in the agent's data folder and needs nothing from here.
    $ini = Join-Path $InstallDir "wslc-ai-agent.ini"
    $line = if (Test-Path -LiteralPath $ini) { Select-String -LiteralPath $ini -Pattern '^\s*ClientPackagesPath\s*=\s*(.+?)\s*$' | Select-Object -First 1 }
    if ($line) { return $line.Matches[0].Groups[1].Value }
    return "C:\Berpiztu\wslc-ai-agent"
}

function Get-AgentBindHost {
    $value = (Get-ItemProperty -Path $RegistryKey -Name BindHost -ErrorAction SilentlyContinue).BindHost
    if ($value) { return $value.Trim() }
    return "127.0.0.1"
}

# A task's PowerShell run through a console that is never drawn (a black window flashed up at every logon and install;
# -WindowStyle Hidden hides it only once it has been drawn). conhost's
# --headless has been there since Windows 10 1809, and WSLC needs Windows 11.
function New-HiddenAction([string]$Arguments, [string]$WorkingDirectory) {
    $conhost = Join-Path $env:SystemRoot "System32\conhost.exe"
    $powershell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    return New-ScheduledTaskAction -Execute $conhost -WorkingDirectory $WorkingDirectory `
        -Argument "--headless `"$powershell`" -NoProfile -NonInteractive -ExecutionPolicy Bypass $Arguments"
}

function Register-LogonTask {
    # The installed copy of this script, even when an update's copy registers it.
    $installedScript = Join-Path $InstallDir "AgentLogonTask.ps1"
    $action = New-HiddenAction "-File `"$installedScript`" -Start" $InstallDir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Principal $principal `
        -Description "Starts WSLC AI Agent in the user's session at logon." | Out-Null
}

function Reset-LogonTask {
    Remove-LogonTask
    Register-LogonTask
    Start-ScheduledTask -TaskName $TaskName
}

function Register-UpdateTask {
    $arguments = "-File `"$ScriptPath`" -Update -Msi `"$Msi`" -Version `"$Version`" -Result `"$Result`""
    $action = New-HiddenAction $arguments (Split-Path -Parent $ScriptPath)
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::FromHours(1))
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Limited
    # No trigger: it runs once, now, and the next update registers it again.
    Register-ScheduledTask -TaskName $UpdateTaskName -Action $action -Settings $settings -Principal $principal -Force `
        -Description "Installs a new version of WSLC AI Agent once the agent has stopped for it." | Out-Null
    Start-ScheduledTask -TaskName $UpdateTaskName
}

function Install-Update {
    # The agent stops itself once this task is scheduled; one that has not let
    # go of its port within a minute is stopped, as -Stop would.
    $deadline = (Get-Date).AddMinutes(1)
    while ((Get-ListenerProcessIds) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
    Stop-Agent

    $log = [IO.Path]::ChangeExtension($Msi, ".log")
    $arguments = "/i `"$Msi`" /qn /norestart WSLC_BINDHOST=$(Get-AgentBindHost) WSLC_AGENTPORT=$(Get-AgentPort) WSLC_PACKAGESFOLDER=`"$(Get-AgentPackagesFolder)`" /l*v `"$log`""
    $code = (Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru).ExitCode
    $ok = $code -eq 0 -or $code -eq 3010
    $outcome = if ($ok) { "ok" } else { "failed" }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Result) | Out-Null
    Set-Content -Path $Result -Value "$outcome|$code|$Version|$log" -Encoding UTF8
    # A failed install rolls the old agent back, but not its logon task, which
    # the old version's removal took away: the task is put back and started,
    # and the old agent reads the result and says so.
    if (-not $ok) { Reset-LogonTask }
}

switch ($PSCmdlet.ParameterSetName) {
    "Register" {
        Stop-Agent
        Reset-LogonTask
    }
    "Unregister" {
        Stop-Agent
        Remove-LogonTask
    }
    "Start" { Start-Agent }
    "Stop" { Stop-Agent }
    "ScheduleUpdate" { Register-UpdateTask }
    "Update" { Install-Update }
}
