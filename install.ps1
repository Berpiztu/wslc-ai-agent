<#
.SYNOPSIS
    Install WSLC AI Agent from its latest release, or update it, in one line:

        irm https://berpiztu.github.io/wslc-ai-agent/install.ps1 | iex

    With the agent already installed, the same line updates it: the latest
    release's installers go into the agent's package folder, and the agent
    installs its own at once; the clients then offer theirs.

    The Windows client instead of the agent:

        & ([scriptblock]::Create((irm https://berpiztu.github.io/wslc-ai-agent/install.ps1))) -Client
.DESCRIPTION
    Downloads the installer of the latest GitHub release into the Downloads
    folder and opens it: a per-user installer, no administrator rights. Says
    first when WSL is missing or too old for WSLC, which the agent needs to
    manage containers.
    Once the agent is installed, it downloads the Windows and Android clients'
    installers into the agent's package folder, from which the agent's web
    page offers them and the agent updates the clients, with its own beside
    them; and offers to install
    the Windows client on this machine too. Then it offers to create the
    publishing container
    (the network, the proxy's map and the nginx proxy that put a container on
    a public HTTPS name): the agent's own Set up, which leaves alone what
    exists. The domain and the rest are set afterwards in Settings > Publish.
    GitHub Pages serves this file (.github/workflows/pages.yml); the README's
    Quick start runs it.
#>
param([switch]$Client)

# In a block of its own: `iex` runs the script in the caller's session, whose
# preferences must stay as they were.
& {
    param([switch]$Client)

    $ErrorActionPreference = "Stop"
    # Windows PowerShell draws a progress bar that slows a large download to a crawl.
    $ProgressPreference = "SilentlyContinue"

    # The installed agent's address, where its installer's wizard left the
    # bind address and port, the agent's own source of them; none when the
    # agent is not installed.
    function InstalledAgent {
        $key = Get-ItemProperty -Path "HKCU:\Software\Berpiztu\wslc-agent\Agent" -ErrorAction SilentlyContinue
        if (-not $key) { return $null }
        $bindHost = if ($key.BindHost -and $key.BindHost -ne "0.0.0.0") { $key.BindHost } else { "127.0.0.1" }
        "http://${bindHost}:$($key.Port)"
    }

    $name = if ($Client) { "wslc-ai-client.msi" } else { "wslc-ai-agent.msi" }
    $product = if ($Client) { "WSLC AI Client" } else { "WSLC AI Agent" }
    $url = "https://github.com/Berpiztu/wslc-ai-agent/releases/latest/download/$name"
    $downloads = Join-Path $env:USERPROFILE "Downloads"
    $msi = Join-Path $downloads $name

    if (-not $Client) {
        # WSLC comes with WSL 2.9.13 and later; without it the agent installs
        # and runs, with no containers to manage.
        $previousUtf8 = $env:WSL_UTF8
        $env:WSL_UTF8 = "1"
        $wslLine = try { & wsl.exe --version 2>$null | Select-Object -First 1 } catch { $null }
        $env:WSL_UTF8 = $previousUtf8
        $wsl = if ("$wslLine" -match '(\d+\.\d+\.\d+)') { [version]$Matches[1] } else { $null }
        $hasWslc = $wsl -and $wsl -ge [version]"2.9.13"
        if (-not $hasWslc) {
            Write-Host "WSLC needs WSL 3.0.1 or later$(if ($wsl) { " (this machine has $wsl)" }). The agent installs anyway; for containers, run: wsl --update" -ForegroundColor Yellow
        }
    }

    # An agent already installed, and answering, is updated rather than
    # installed again: the newer installers go into its package folder, and
    # the agent installs its own there at once, as Settings > Update's Update
    # now does; the clients then offer theirs. One that does not answer is
    # installed again: its installer upgrades it in place.
    if (-not $Client -and ($agent = InstalledAgent)) {
        $status = try { Invoke-RestMethod -Uri "$agent/api/v1/agent/update" -TimeoutSec 15 } catch { $null }
        if ($status) {
            $running = [version](($status.version -split '[^0-9.]')[0])
            $latest = [version](Invoke-RestMethod -Uri "https://api.github.com/repos/Berpiztu/wslc-ai-agent/releases/latest" -TimeoutSec 30).tag_name.TrimStart("v")
            if ($latest -le $running) {
                Write-Host "WSLC AI Agent $running is installed: the latest release. Nothing to update." -ForegroundColor Green
                return
            }
            Write-Host "Updating WSLC AI Agent $running to $latest..." -ForegroundColor Cyan
            New-Item -ItemType Directory -Force -Path $status.packageFolder | Out-Null
            foreach ($package in @("wslc-ai-agent.msi", "wslc-ai-client.msi", "wslc-ai-client.apk")) {
                Write-Host "Downloading $package into the agent's package folder..." -ForegroundColor Cyan
                Invoke-WebRequest -Uri "https://github.com/Berpiztu/wslc-ai-agent/releases/latest/download/$package" -OutFile (Join-Path $status.packageFolder $package) -UseBasicParsing
            }
            try {
                Invoke-RestMethod -Method Post -Uri "$agent/api/v1/agent/update" -TimeoutSec 30 | Out-Null
                Write-Host "The agent installs $latest now: it waits for any transfer in progress, and starts again as $latest in a minute or two. The clients then offer their update." -ForegroundColor Green
            } catch {
                $reason = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { $_.Exception.Message }
                Write-Host "The installers are in $($status.packageFolder), but the agent did not start its update: $reason" -ForegroundColor Yellow
                Write-Host "Settings > Update shows why, and has Update now." -ForegroundColor Yellow
            }
            return
        }
    }

    New-Item -ItemType Directory -Force -Path $downloads | Out-Null
    Write-Host "Downloading $product from the latest release..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $msi -UseBasicParsing
    Write-Host "Opening the installer: $msi" -ForegroundColor Cyan
    Start-Process -FilePath $msi -Wait

    if ($Client) {
        Write-Host "Done. Open WSLC AI Client from the Start menu." -ForegroundColor Green
        return
    }

    $agent = InstalledAgent
    if (-not $agent) {
        Write-Host "The agent was not installed." -ForegroundColor Yellow
        return
    }
    Write-Host "Done. The agent starts at logon; its dashboard: $agent" -ForegroundColor Green

    # The agent starts right after its installer; give it a minute to answer.
    $deadline = (Get-Date).AddSeconds(60)
    $up = $false
    while (-not $up -and (Get-Date) -lt $deadline) {
        try { Invoke-RestMethod -Uri "$agent/api/v1/health" -TimeoutSec 5 | Out-Null; $up = $true } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $up) {
        Write-Host "The agent does not answer at $agent yet. Copy wslc-ai-client.msi and wslc-ai-client.apk from the release into its package folder, and Settings > Publish > Set up creates the publishing container." -ForegroundColor Yellow
        return
    }

    # The three installers go into the agent's package folder, as an update
    # leaves them: the web UI offers the clients' to download from there, the
    # agent updates the clients from there, and its own waits there beside
    # them (the version it runs, so it updates nothing).
    try {
        $folder = (Invoke-RestMethod -Uri "$agent/api/v1/agent/update" -TimeoutSec 30).packageFolder
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        Write-Host "Copying wslc-ai-agent.msi into the agent's package folder..." -ForegroundColor Cyan
        Copy-Item -LiteralPath $msi -Destination (Join-Path $folder "wslc-ai-agent.msi") -Force
        foreach ($package in @("wslc-ai-client.msi", "wslc-ai-client.apk")) {
            Write-Host "Downloading $package into the agent's package folder..." -ForegroundColor Cyan
            Invoke-WebRequest -Uri "https://github.com/Berpiztu/wslc-ai-agent/releases/latest/download/$package" -OutFile (Join-Path $folder $package) -UseBasicParsing
        }
        Write-Host "The Windows and Android clients can now be downloaded from the agent's web page ($folder)." -ForegroundColor Green

        # Downloaded by PowerShell, the client's installer opens with no
        # SmartScreen warning; it connects to this machine's agent by default.
        $installClient = try { Read-Host "Install the Windows client on this machine too? [y/N]" } catch { "" }
        if ($installClient -match '^\s*(y|yes)\s*$') {
            Write-Host "Opening the Windows client's installer..." -ForegroundColor Cyan
            Start-Process -FilePath (Join-Path $folder "wslc-ai-client.msi") -Wait
            Write-Host "Done. Open WSLC AI Client from the Start menu." -ForegroundColor Green
        }
    } catch {
        Write-Host "The clients' installers were not downloaded: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "Copy wslc-ai-client.msi and wslc-ai-client.apk from the release into the package folder shown in Settings > Update." -ForegroundColor Yellow
    }

    # Set up runs wslc, so it needs WSLC; without it the button in Settings
    # does it later.
    if (-not $hasWslc) { return }
    Write-Host ""
    Write-Host "Publishing puts a container on a public HTTPS name under your own domain,"
    Write-Host "through an nginx proxy container this agent manages."
    $answer = try { Read-Host "Create the publishing container now? [y/N]" } catch { "" }
    if ($answer -notmatch '^\s*(y|yes)\s*$') {
        Write-Host "Skipped. Settings > Publish > Set up creates it whenever you want." -ForegroundColor DarkGray
        return
    }

    # WSLC does not start its session on its own, and Set up runs in it: the
    # session the agent works in is started first when it is not running.
    try {
        $sessions = Invoke-RestMethod -Uri "$agent/api/v1/sessions" -TimeoutSec 60
        $selected = @($sessions.sessions) | Where-Object { $_.name -eq $sessions.selected } | Select-Object -First 1
        if (-not ($selected -and $selected.active)) {
            Write-Host "Starting the WSLC session $($sessions.selected)..." -ForegroundColor Cyan
            Invoke-RestMethod -Method Post -Uri "$agent/api/v1/sessions/start" -ContentType "application/json" -Body '{"name":""}' -TimeoutSec 300 | Out-Null
        }
    } catch {
        Write-Host "The WSLC session could not be started: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Start it from the agent's session menu, then Settings > Publish > Set up creates the publishing container." -ForegroundColor Yellow
        return
    }

    Write-Host "Creating the publishing container (the first time, WSLC downloads nginx)..." -ForegroundColor Cyan
    try {
        $setup = Invoke-RestMethod -Method Post -Uri "$agent/api/v1/publishing/setup" -TimeoutSec 600
    } catch {
        Write-Host "Set up failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Settings > Publish > Set up tries again, and says why." -ForegroundColor Yellow
        return
    }
    Write-Host "  network  $(if ($setup.networkCreated) { 'created' } else { 'already there' })"
    Write-Host "  map      $(if ($setup.mapWritten) { 'written' } else { 'already there' })"
    Write-Host "  proxy    $(if ($setup.proxyCreated) { 'created' } else { 'already there' })"
    foreach ($note in @($setup.notes)) { if ($note) { Write-Host "  $note" -ForegroundColor DarkGray } }
    Write-Host ""
    Write-Host "Now set your domain and name suffix in Settings > Publish: $agent/settings" -ForegroundColor Green
    Write-Host "The whole route, domain, certificate and VPS: https://github.com/Berpiztu/wslc-ai-agent/blob/main/docs/developer/remote-access.md" -ForegroundColor Green
} -Client:$Client
