<#
.SYNOPSIS
    The plugin's pages on this machine, with no container: for working on them.
.DESCRIPTION
    Makes a virtual environment beside this file the first time (.venv, with
    the image's own packages: Flask, waitress, cryptography), then runs dev.py:
    http://127.0.0.1:8091, the pages as nginx serves them in the container, on
    their own test data (.dev-data). A page or a stylesheet saved shows on the
    next reload; a .py saved restarts the server. Ctrl+C stops it.

    -Reset forgets the test data, so the two sample names come back as new.
#>
param([switch]$Reset)

$ErrorActionPreference = 'Stop'
$venv = Join-Path $PSScriptRoot '.venv'
$python = Join-Path $venv 'Scripts\python.exe'

if (-not (Test-Path $python)) {
    Write-Host 'Making the virtual environment (once)...' -ForegroundColor Cyan
    python -m venv $venv
    & $python -m pip install --quiet --disable-pip-version-check flask waitress cryptography
}

if ($Reset) {
    Remove-Item -Recurse -Force (Join-Path $PSScriptRoot '.dev-data') -ErrorAction SilentlyContinue
}

& $python (Join-Path $PSScriptRoot 'dev.py')
