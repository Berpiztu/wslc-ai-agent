<#
.SYNOPSIS
    Download and install WSLC AI Agent from its latest release, in one line:

        irm https://berpiztu.github.io/wslc-ai-agent/install.ps1 | iex

    The Windows client instead of the agent:

        & ([scriptblock]::Create((irm https://berpiztu.github.io/wslc-ai-agent/install.ps1))) -Client
.DESCRIPTION
    Downloads the installer of the latest GitHub release into the Downloads
    folder and opens it: a per-user installer, no administrator rights. Says
    first when WSL is missing or too old for WSLC, which the agent needs to
    manage containers.
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
        if (-not $wsl -or $wsl -lt [version]"2.9.13") {
            Write-Host "WSLC needs WSL 3.0.1 or later$(if ($wsl) { " (this machine has $wsl)" }). The agent installs anyway; for containers, run: wsl --update" -ForegroundColor Yellow
        }
    }

    New-Item -ItemType Directory -Force -Path $downloads | Out-Null
    Write-Host "Downloading $product from the latest release..." -ForegroundColor Cyan
    Invoke-WebRequest -Uri $url -OutFile $msi -UseBasicParsing
    Write-Host "Opening the installer: $msi" -ForegroundColor Cyan
    Start-Process -FilePath $msi -Wait

    if ($Client) {
        Write-Host "Done. Open WSLC AI Client from the Start menu." -ForegroundColor Green
    } else {
        Write-Host "Done. The agent starts at logon; its dashboard: http://127.0.0.1:8069" -ForegroundColor Green
    }
} -Client:$Client
