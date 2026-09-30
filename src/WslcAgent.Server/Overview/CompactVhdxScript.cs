namespace WslcAgent.Server.Overview;

/// <summary>
/// The PowerShell the <c>compact-vhdx</c> terminal job runs.
/// Typed into the host terminal as
/// <c>powershell -File</c>, so everything it prints lands where the user
/// watches. It never terminates a session: it checks the session list first and
/// stops when one is active. Optimize-VHD only reclaims blocks the guest has
/// discarded, so it trims first in a temporary session on the same storage
/// (<c>wslc system session enter</c>, gone when its shell exits), waits for that
/// session to be gone, then runs <c>Optimize-VHD -Mode Full</c> in a
/// UAC-elevated PowerShell whose log it relays line by line, and prints the
/// sizes before and after. The shell stays open afterwards.
/// </summary>
public static class CompactVhdxScript
{
    public const string FileName = "wslc-ai-agent-compact-vhdx.ps1";
    private const string TrimSession = "wslc-agent-trim";

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
            "Write-Host ('[1/3] Checking session ' + $session + ' (compaction never terminates one).') -ForegroundColor Yellow",
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
            "  Write-Host ('Other sessions are running; they hold their own VHDX, not this one:') -ForegroundColor DarkGray",
            "  $sessionRows | ForEach-Object { Write-Host ('  ' + $_) -ForegroundColor DarkGray }",
            "}",
            "Write-Host ('Session ' + $session + ' is not running.')",
            "$before = (Get-Item -LiteralPath $path).Length",
            "Write-Host ('Size before: {0:N2} GB' -f ($before / 1GB))",
            "Write-Host ''",
            "Write-Host '[2/3] fstrim in a temporary session on the same storage (Optimize-VHD only reclaims discarded blocks).' -ForegroundColor Yellow",
            $"Write-Host ('      wslc system session enter --name {TrimSession} ' + $storageDir)",
            "$LASTEXITCODE = 0",
            "$esc = [char]27",
            "$bel = [char]7",
            // The temporary session's shell echoes a coloured prompt: its colour and
            // cursor codes are stripped and blank lines dropped, so the trim output
            // relays as cleanly as the script's own [n/3] lines.
            $"'fstrim -av; exit' | & $wslc system session enter --name {TrimSession} $storageDir 2>&1 | ForEach-Object {{",
            "  $l = $_ -replace ($esc + '\\[[0-9;?]*[A-Za-z]'), ''",
            "  $l = ($l -replace ($esc + '\\][^' + $bel + ']*' + $bel), '')",
            // The session's shell ends its lines the Linux way and leaves the console
            // without the translation that returns the carriage, so each relayed line
            // started where the last had ended — a staircase down the window. The
            // block is split into its lines, and each one is written after a carriage
            // return of its own, which puts it at the left margin whatever state the
            // session left the console in.
            "  foreach ($part in ($l -split \"`r`n|`n|`r\")) {",
            "    $line = $part.TrimEnd()",
            "    if ($line.Trim().Length -gt 0) { Write-Host ([char]13 + '  | ' + $line) }",
            "  }",
            "}",
            // And the carriage stays there for what the script prints next.
            "Write-Host -NoNewline ([char]13)",
            "if ($LASTEXITCODE -ne 0) {",
            "  Write-Host ('fstrim session exit code: ' + $LASTEXITCODE + ' (continuing; nothing may be reclaimed).') -ForegroundColor Yellow",
            "} else {",
            "  Write-Host 'Trim done.' -ForegroundColor Green",
            "}",
            "$deadline = (Get-Date).AddSeconds(90)",
            "$stillActive = $true",
            "while ((Get-Date) -lt $deadline) {",
            "  $LASTEXITCODE = 0",
            "  $rows = @(& $wslc system session list 2>&1 | Select-Object -Skip 1 | Where-Object { $_ -and $_.ToString().Trim() })",
            "  if ($LASTEXITCODE -eq 0 -and $rows.Count -eq 0) { $stillActive = $false; break }",
            "  Start-Sleep -Seconds 3",
            "}",
            "if ($stillActive) {",
            "  Write-Host 'The temporary trim session is still listed after 90 s; not compacting.' -ForegroundColor Red",
            "  Write-Host 'Check wslc system session list, then run Compact VHDX again.' -ForegroundColor Yellow",
            "  return",
            "}",
            "Write-Host ''",
            "Write-Host '[3/3] Optimize-VHD -Mode Full in an elevated PowerShell (its output is relayed here).' -ForegroundColor Yellow",
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
            "      Write-Host ('  ... still running (' + $elapsed + ' s)') -ForegroundColor DarkGray",
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
