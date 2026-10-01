<#
.SYNOPSIS
    Publish new objects' defaults or a new default dashboard between
    releases, without a new installer.
.DESCRIPTION
    The two files travel beside the installers of the latest GitHub release,
    each with its own version (docs/dashboard-defaults.md). This replaces
    them there:

      1. Stops when the defaults have uncommitted changes: what is published
         is what the repository holds.
      2. Writes both files into dist from
         src\WslcAgent.Server\Overview, with the versions of
         defaults-versions.json.
      3. Stops when neither version is above the one published. The default
         dashboard's rises by itself with each Save as default on a
         development agent; the objects' defaults' is raised by hand in
         defaults-versions.json (and either minAgentVersion when the file uses
         something an older agent does not know). Commit, and run this again.
      4. Uploads the files whose version went up to the latest release,
         replacing the old ones.

    The update line (install.ps1) then puts them in each agent's package
    folder, and the dashboard's Tools offers to load them.
.EXAMPLE
    .\publish-defaults.ps1
#>
$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

$overview = "src\WslcAgent.Server\Overview"
$sources = @("object-defaults.json", "dashboard-v2.5.default.json", "defaults-versions.json") | ForEach-Object { Join-Path $overview $_ }

# 1. Only what is committed is published.
$dirty = @(git status --porcelain -- @sources)
if ($dirty.Count -gt 0) {
    throw "Commit the defaults first; what is published is what the repository holds:`n$($dirty -join "`n")"
}

# 2. The two files, as a release carries them.
$files = Write-WslcAgentDefaultsFiles -Destination (Join-Path $RepoRoot "dist")

# 3. Only a file whose version went up replaces the published one.
$tag = gh release view --json tagName --jq .tagName
if ($LASTEXITCODE -ne 0 -or -not $tag) { throw "No GitHub release to publish beside: gh release view failed." }
$published = Join-Path $env:TEMP "wslc-defaults-published"
Remove-Item -Recurse -Force -LiteralPath $published -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $published | Out-Null

$newer = @()
foreach ($file in $files) {
    $name = Split-Path -Leaf $file
    $mine = [int]([IO.File]::ReadAllText($file) | ConvertFrom-Json).version
    # A release made before the defaults travelled has none: version 0.
    try { gh release download $tag --pattern $name --dir $published 2>$null } catch { }
    $theirs = if (Test-Path -LiteralPath (Join-Path $published $name)) {
        [int]([IO.File]::ReadAllText((Join-Path $published $name)) | ConvertFrom-Json).version
    } else { 0 }
    if ($mine -gt $theirs) {
        Write-Host "$name v$mine replaces v$theirs on $tag." -ForegroundColor Cyan
        $newer += $file
    } else {
        Write-Host "$name stays v$theirs on $tag (the repository's is v$mine)." -ForegroundColor DarkGray
    }
}

if ($newer.Count -eq 0) {
    throw "Nothing to publish: raise the version of the file that changed in $overview\defaults-versions.json, commit, and run this again."
}

# 4. Uploaded over the old ones.
gh release upload $tag @newer --clobber
if ($LASTEXITCODE -ne 0) { throw "gh release upload failed with exit code $LASTEXITCODE" }
Write-Host "Published on $tag. The update line puts them in each agent's package folder; the dashboard's Tools offers to load them." -ForegroundColor Green
