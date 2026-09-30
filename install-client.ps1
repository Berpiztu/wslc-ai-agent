<#
.SYNOPSIS
    Install the Windows client, WSLC AI Client, from the latest release, in
    one line:

        irm https://berpiztu.github.io/wslc-ai-agent/install-client.ps1 | iex

    install.ps1 does the work, with -Client; this only spares its longer line.
    GitHub Pages serves both (.github/workflows/pages.yml).
#>
& ([scriptblock]::Create((Invoke-RestMethod -Uri "https://berpiztu.github.io/wslc-ai-agent/install.ps1"))) -Client
