namespace WslcAgent.Server.Overview;

/// <summary>
/// The PowerShell the <c>compact-vhdx</c> terminal job runs.
/// Typed into the host terminal as
/// <c>powershell -File</c>, so everything it prints lands where the user
/// watches. It never terminates a session: it checks the session list first and
/// stops when one is active. A VHDX only shrinks by what is free inside it, so
/// it first reads what the store holds in a temporary session on the same
/// storage (<c>wslc system session enter</c>, gone when its shell exits), says
/// what no image and no container uses, and removes it only on a yes
/// (<see cref="StoreCleanupScript"/>). Optimize-VHD only reclaims blocks the
/// guest has discarded, so it trims there too, waits for that session to be
/// gone, then runs <c>Optimize-VHD -Mode Full</c> in a UAC-elevated PowerShell
/// whose log it relays line by line, and prints the sizes before and after.
/// The shell stays open afterwards.
/// </summary>
public static class CompactVhdxScript
{
    public const string FileName = "wslc-ai-agent-compact-vhdx.ps1";

    /// <summary>
    /// Where the script writes its process id as it starts, beside itself: how
    /// the agent tells a job still under way, whose window is shown again as
    /// it stands, from one that ended, whose window starts a new one.
    /// </summary>
    public const string PidFileName = "wslc-ai-agent-compact-vhdx.pid";
    /// <summary>The temporary session the script opens on the stopped store; a stop is asked of it by this name.</summary>
    public const string TrimSession = "wslc-agent-trim";
    private const string ModePlaceholder = "__MODE__";

    public static string Build(string storagePath, string session, string wslcExecutable)
    {
        var path = storagePath.Trim();
        if (path.Length == 0)
        {
            throw new ArgumentException("A VHDX path is required.", nameof(storagePath));
        }

        var storageDir = Path.GetDirectoryName(path) ?? path;
        string[] elevated =
        [
            "$ErrorActionPreference = 'Continue'",
            "$log = '__WSLC_LOG__'",
            $"$path = {Quote(path)}",
            "function Note($text) { Add-Content -LiteralPath $log -Value $text -Encoding UTF8 }",
            "Note ('Elevated as ' + [Security.Principal.WindowsIdentity]::GetCurrent().Name)",
            "Note ('Optimize-VHD -Path ' + $path + ' -Mode Full')",
            "try {",
            "  Optimize-VHD -Path $path -Mode Full -ErrorAction Stop",
            "  Note 'Optimize-VHD: OK'",
            "  exit 0",
            "} catch {",
            "  Note ('Optimize-VHD failed: ' + $_.Exception.Message)",
            "  exit 1",
            "}",
        ];

        string[] lines =
        [
            "$ErrorActionPreference = 'Continue'",
            "$Host.UI.RawUI.WindowTitle = 'WSLC compact VHDX'",
            $"Set-Content -LiteralPath (Join-Path $PSScriptRoot {Quote(PidFileName)}) -Value $PID",
            $"$path = {Quote(path)}",
            $"$session = {Quote(session.Trim())}",
            $"$storageDir = {Quote(storageDir)}",
            $"$wslc = {Quote(wslcExecutable)}",
            "if (-not (Test-Path -LiteralPath $wslc)) { $wslc = 'wslc' }",
            "Write-Host '== WSLC compact VHDX ==' -ForegroundColor Cyan",
            "Write-Host ('VHDX: ' + $path)",
            "if (-not (Test-Path -LiteralPath $path)) {",
            "  Write-Host 'VHDX file not found.' -ForegroundColor Red",
            "  return",
            "}",
            "Write-Host ('[1/4] Checking session ' + $session + ' (compaction never terminates one).') -ForegroundColor Yellow",
            "$LASTEXITCODE = 0",
            "$sessionRows = @(& $wslc system session list 2>&1 | Select-Object -Skip 1 | Where-Object { $_ -and $_.ToString().Trim() })",
            "if ($LASTEXITCODE -ne 0) {",
            "  Write-Host 'Could not list WSLC sessions; not compacting.' -ForegroundColor Red",
            "  $sessionRows | ForEach-Object { Write-Host ('  ' + $_) }",
            "  return",
            "}",
            // This VHDX belongs to one session and only that one holds it open;
            // the display name is the row's last field, the others are its id
            // and its creator's pid.
            "$mine = @($sessionRows | Where-Object { (($_.ToString().Trim()) -split '\\s+')[-1] -eq $session })",
            "if ($mine.Count -gt 0) {",
            "  Write-Host ('Compaction blocked: session ' + $session + ' is running and holds this VHDX open.') -ForegroundColor Red",
            "  $mine | ForEach-Object { Write-Host ('  ' + $_) }",
            "  Write-Host 'Stop it from the Session panel first, then run Compact VHDX again.' -ForegroundColor Yellow",
            "  return",
            "}",
            "if ($sessionRows.Count -gt 0) {",
            "  Write-Host ('Other sessions are running; they hold their own VHDX, not this one:') -ForegroundColor Cyan",
            "  $sessionRows | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor Cyan }",
            "}",
            "Write-Host ('Session ' + $session + ' is not running.')",
            "$before = (Get-Item -LiteralPath $path).Length",
            "Write-Host ('Size before: {0:N2} GB' -f ($before / 1GB))",
            $"$typed = {Quote(StoreCleanupScript.Typed(ModePlaceholder))}",
            "$esc = [char]27",
            "$bel = [char]7",
            // The store's script, typed into a temporary session on the storage;
            // what comes back is the lines it means to be read. The session's
            // shell echoes what is typed behind a coloured prompt: colour and
            // cursor codes are stripped, and only a line the script marked is
            // passed on, without its mark.
            "function Enter-Store($mode) {",
            $"  $typed.Replace({Quote(ModePlaceholder)}, $mode) | & $wslc system session enter --name {TrimSession} $storageDir 2>&1 | ForEach-Object {{",
            "    $l = \"$_\" -replace ($esc + '\\[[0-9;?]*[A-Za-z]'), ''",
            "    $l = ($l -replace ($esc + '\\][^' + $bel + ']*' + $bel), '')",
            "    foreach ($part in ($l -split \"`r`n|`n|`r\")) {",
            "      $line = $part.Trim()",
            $"      if ($line.StartsWith({Quote(StoreCleanupScript.Mark)})) {{ $line.Substring({StoreCleanupScript.Mark.Length}) }}",
            "    }",
            "  }",
            "}",
            // The session's shell ends its lines the Linux way and leaves the console
            // without the translation that returns the carriage, so each line written
            // while it is up started where the last had ended — a staircase down the
            // window. Each one is written after a carriage return of its own, which
            // puts it at the left margin whatever state the session left the console in.
            "$script:ticking = $false",
            "function End-Ticks { if ($script:ticking) { Write-Host ''; $script:ticking = $false } }",
            "function Show($text, $color = 'Gray') { End-Ticks; Write-Host ([char]13 + $text) -ForegroundColor $color }",
            // How far the work has got, written over itself on one line: a
            // counter that moves, where a line per step would run off the window.
            "function Tick($text) { Write-Host -NoNewline ([char]13 + '  | ' + $text + '          ') -ForegroundColor Cyan; $script:ticking = $true }",
            "function Size($bytes) { '{0,9:N2} GB' -f ([long]$bytes / 1GB) }",
            // The temporary session holds the VHDX open until it is gone, and wslc
            // goes on listing a session for a moment after its shell has left.
            "function Wait-StoreFree {",
            "  $deadline = (Get-Date).AddSeconds(90)",
            "  while ((Get-Date) -lt $deadline) {",
            "    $global:LASTEXITCODE = 0",
            "    $rows = @(& $wslc system session list 2>&1 | Select-Object -Skip 1 | Where-Object { $_ -and $_.ToString().Trim() })",
            $"    $held = @($rows | Where-Object {{ (($_.ToString().Trim()) -split '\\s+')[-1] -eq {Quote(TrimSession)} }})",
            "    if ($global:LASTEXITCODE -eq 0 -and $held.Count -eq 0) { return $true }",
            "    Start-Sleep -Seconds 3",
            "  }",
            "  Write-Host 'The temporary session is still listed after 90 s; not compacting.' -ForegroundColor Red",
            "  Write-Host 'Check wslc system session list, then run Compact VHDX again.' -ForegroundColor Yellow",
            "  return $false",
            "}",
            "Write-Host ''",
            "Write-Host '[2/4] Reading what the store holds, in a temporary session on it (nothing is changed).' -ForegroundColor Yellow",
            $"Write-Host ('      wslc system session enter --name {TrimSession} ' + $storageDir)",
            "$facts = @{}",
            "Write-Host '      Starting the temporary session...' -ForegroundColor Cyan",
            $"Enter-Store {Quote(StoreCleanupScript.Report)} | ForEach-Object {{",
            "  $fields = $_ -split ' '",
            "  if ($fields[0] -eq 'STEP') { Show ('  | ' + $_.Substring(5)) 'Cyan' }",
            "  elseif ($fields[0] -eq 'TICK') { Tick $_.Substring(5) }",
            "  else { $facts[$fields[0]] = $fields }",
            "}",
            "End-Ticks",
            // And the carriage stays at the margin for what the script prints next.
            "Write-Host -NoNewline ([char]13)",
            "if ($facts.ContainsKey('STOPPED')) {",
            "  Write-Host 'Stopped as asked: the store was only read, nothing was changed.' -ForegroundColor Yellow",
            "  [void](Wait-StoreFree)",
            "  return",
            "}",
            "$removable = 0",
            "if ($facts.ContainsKey('USED') -and $facts.ContainsKey('IMAGES') -and $facts.ContainsKey('CONTAINERS') -and $facts.ContainsKey('UNUSED') -and $facts.ContainsKey('BUILD')) {",
            "  $used = [long]$facts['USED'][1]",
            "  $images = [long]$facts['IMAGES'][3]",
            "  $unused = [long]$facts['UNUSED'][2]",
            "  $build = [long]$facts['BUILD'][2]",
            "  $removable = $unused + $build",
            "  Write-Host ((Size $used) + '  in use inside the VHDX')",
            "  Write-Host ((Size $images) + '  kept: ' + $facts['IMAGES'][1] + ' images (' + $facts['IMAGES'][2] + ' layers)')",
            "  Write-Host ((Size ([Math]::Max([long]0, $used - $images - $removable))) + '  kept: volumes, ' + $facts['CONTAINERS'][1] + ' containers and the rest')",
            "  Write-Host ((Size $unused) + '  can go: ' + $facts['UNUSED'][1] + ' layers no image and no container uses') -ForegroundColor Cyan",
            "  if ([int]$facts['CONTAINERS'][2] -gt 0) {",
            "    Write-Host ('              ' + $facts['CONTAINERS'][2] + ' containers long removed left their own layer behind, which kept them') -ForegroundColor Cyan",
            "  }",
            "  Write-Host ((Size $build) + '  can go: the build cache (' + $facts['BUILD'][1] + ' folders)') -ForegroundColor Cyan",
            "} elseif ($facts.ContainsKey('LAYOUT')) {",
            "  Write-Host 'The store is not laid out the way this tool knows; nothing will be removed from it.' -ForegroundColor Yellow",
            "} else {",
            "  Write-Host 'The temporary session did not report what the store holds; nothing will be removed from it.' -ForegroundColor Yellow",
            "}",
            // Removing is asked, every time, with what it costs: nothing is
            // removed on any answer but yes, and q leaves with the store only
            // read, which is how to look at what it holds and change nothing.
            $"$mode = {Quote(StoreCleanupScript.Trim)}",
            "if ($removable -gt 0) {",
            "  Write-Host ''",
            "  Write-Host 'Removing them deletes those layers and the whole build cache: the next build of each image starts from scratch.'",
            "  Write-Host 'Images, containers and volumes are not touched.'",
            "  Write-Host 'WARNING: this cannot be undone.' -ForegroundColor Red",
            "  Write-Host '  y  remove them, then compact'",
            "  Write-Host '  n  compact without removing anything'",
            "  Write-Host '  q  stop here: nothing is changed'",
            "  $answer = Read-Host ('Remove ' + (Size $removable).Trim() + ' before compacting? [y/N/q]')",
            "  if ($answer -match '^\\s*q(uit)?\\s*$') { Write-Host 'Stopped: the store was only read, nothing was changed.' -ForegroundColor Cyan; return }",
            $"  if ($answer -match '^\\s*y(es)?\\s*$') {{ $mode = {Quote(StoreCleanupScript.Clean)} }} else {{ Write-Host 'Nothing removed.' }}",
            "}",
            "if (-not (Wait-StoreFree)) { return }",
            "Write-Host ''",
            $"if ($mode -eq {Quote(StoreCleanupScript.Clean)}) {{",
            "  Write-Host '[3/4] Removing, then fstrim, in a temporary session on the same storage (Optimize-VHD only reclaims discarded blocks).' -ForegroundColor Yellow",
            "} else {",
            "  Write-Host '[3/4] fstrim in a temporary session on the same storage (Optimize-VHD only reclaims discarded blocks).' -ForegroundColor Yellow",
            "}",
            "$ended = $false",
            "$stopped = $false",
            "Write-Host '      Starting the temporary session...' -ForegroundColor Cyan",
            "Enter-Store $mode | ForEach-Object {",
            "  if ($_ -like 'TRIM *') { Show ('  | ' + $_.Substring(5)) }",
            "  elseif ($_ -like 'STEP *') { Show ('  | ' + $_.Substring(5)) 'Cyan' }",
            "  elseif ($_ -like 'TICK *') { Tick $_.Substring(5) }",
            "  elseif ($_ -like 'FREED *') { Show ('  | Removed: ' + (Size $_.Substring(6)).Trim()) 'Green' }",
            "  elseif ($_ -eq 'STOPPED') { $stopped = $true }",
            "  elseif ($_ -eq 'END') { $ended = $true }",
            "}",
            "End-Ticks",
            "Write-Host -NoNewline ([char]13)",
            // Stopped from the window's Stop: it left between two layers, so
            // nothing is half removed; the VHDX is not compacted, and the next
            // run goes on from where this one stopped.
            "if ($stopped) {",
            "  Write-Host 'Stopped as asked, between two steps: nothing is left half done. The VHDX was not compacted.' -ForegroundColor Yellow",
            "  Write-Host 'Run Compact VHDX again to finish.' -ForegroundColor Yellow",
            "  [void](Wait-StoreFree)",
            "  return",
            "}",
            "if ($ended) {",
            "  Write-Host 'Trim done.' -ForegroundColor Green",
            "} else {",
            "  Write-Host 'The temporary session left before its work was done (continuing; less may be reclaimed).' -ForegroundColor Yellow",
            "}",
            "if (-not (Wait-StoreFree)) { return }",
            "Write-Host ''",
            "Write-Host '[4/4] Optimize-VHD -Mode Full in an elevated PowerShell (its output is relayed here).' -ForegroundColor Yellow",
            "Write-Host '      If Windows asks for consent (UAC), accept it on the server desktop.' -ForegroundColor Yellow",
            "$log = Join-Path $env:TEMP ('wslc-agent-optimize-vhd-' + [guid]::NewGuid().ToString('N') + '.log')",
            // The log path lands inside a single-quoted literal of the elevated
            // script, so any quote in it is doubled first.
            $"$inner = {Quote(string.Join('\n', elevated))}",
            "$inner = $inner.Replace('__WSLC_LOG__', ([string]$log).Replace(\"'\", \"''\"))",
            "$innerEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($inner))",
            "$elevated = $null",
            "try {",
            "  $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $innerEncoded)",
            "} catch {",
            "  Write-Host ('Elevation was cancelled or failed: ' + $_.Exception.Message) -ForegroundColor Red",
            "}",
            "if ($elevated) {",
            "  Write-Host ('Elevated PowerShell started (pid ' + $elevated.Id + '); waiting for it to finish...')",
            "  $shown = 0",
            "  $started = Get-Date",
            "  $nextBeat = 15",
            "  while (-not $elevated.HasExited) {",
            "    Start-Sleep -Milliseconds 500",
            "    if (Test-Path -LiteralPath $log) {",
            "      $lines = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue)",
            "      for ($i = $shown; $i -lt $lines.Count; $i++) { Write-Host ('  | ' + $lines[$i]) }",
            "      $shown = $lines.Count",
            "    }",
            "    $elapsed = [int]((Get-Date) - $started).TotalSeconds",
            "    if ($elapsed -ge $nextBeat) {",
            "      Write-Host ('  ... still running (' + $elapsed + ' s)') -ForegroundColor Cyan",
            "      $nextBeat += 15",
            "    }",
            "  }",
            "  if (Test-Path -LiteralPath $log) {",
            "    $lines = @(Get-Content -LiteralPath $log -ErrorAction SilentlyContinue)",
            "    for ($i = $shown; $i -lt $lines.Count; $i++) { Write-Host ('  | ' + $lines[$i]) }",
            "    Remove-Item -LiteralPath $log -ErrorAction SilentlyContinue",
            "  } else {",
            "    Write-Host 'The elevated PowerShell did not report back (UAC cancelled?).' -ForegroundColor Yellow",
            "  }",
            "  Write-Host ('Elevated PowerShell exit code: ' + $elevated.ExitCode)",
            "}",
            "$after = (Get-Item -LiteralPath $path).Length",
            "Write-Host ('Size after: {0:N2} GB (freed {1:N2} GB)' -f ($after / 1GB), (($before - $after) / 1GB))",
            "Write-Host ''",
            "Write-Host 'Done. Refresh the System page for the new sizes.' -ForegroundColor Cyan",
        ];

        return string.Join('\n', lines);
    }

    /// <summary>A PowerShell single-quoted literal: quotes inside are doubled.</summary>
    internal static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
