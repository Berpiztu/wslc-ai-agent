<#
.SYNOPSIS
    Publish a release: raise the release version above every build so far,
    and commit, tag and push it; GitHub Actions builds the installers and
    publishes the release (.github/workflows/release.yml). -Local builds and
    uploads them from this machine instead.
.DESCRIPTION
    The version in the repository is the last release's (Directory.Build.props
    for the agent, the client's csproj for the client and its Android
    versionCode); everyday builds raise only this checkout's own, in
    private\version.props. A release raises the repository's above both:

      1. Takes what this checkout holds: goes to main, from whatever branch
         it was run on (one with commits of its own has to be merged or
         offered as a pull request first), and commits the changes not
         committed yet, with a message it writes itself; new files are
         listed and taken only when the person says so.
      2. Brings main up to date (rebasing that commit on top). Then, for
         each open pull request into main, waits for its
         checks and asks whether to merge it (squash); one whose checks fail
         is reported and left alone. main is brought up to date again with
         what was merged, so the release carries it.
      3. Writes the new versions: the agent's and the client's next patch
         above the higher of the release's and private\version.props, or
         -Version for both; the versionCode one above the higher of the two.
      4. Commits the two files, tags v<agent version> and pushes both.
      5. GitHub Actions sees the tag and builds the agent installer
         (-Release), the Windows client installer and the APK at exactly the
         release's versions, then creates the GitHub release with the three
         installers and the two defaults files. Nothing is uploaded from this
         machine. A failed build leaves the tag without a release: fix it and
         run the workflow again on the tag.

    With -Local, as before the workflow existed: builds the three here first
    (a failure puts the versions back and commits nothing), then commits,
    tags (marked built locally, so the workflow leaves it alone) and pushes;
    with -Publish it creates the release and uploads the files from here.
    Between releases, publish-defaults.ps1 replaces the two defaults files
    alone.

    The APK is signed with the same key everywhere (see
    docs\developer\private-files.md): here private\android.keystore, on
    GitHub the repository's secrets; a release signed with another key cannot
    update the installed app.
.PARAMETER Version
    The release's version for the agent and the client, x.y.z; it has to be
    above the agent's and the client's current ones.
.PARAMETER Local
    Build the installers here and leave GitHub Actions out of it.
.PARAMETER Publish
    With -Local, also create the GitHub release (gh must be signed in) and
    upload the installers to it. Without -Local the workflow always does.
.EXAMPLE
    .\deploy-release.ps1
.EXAMPLE
    .\deploy-release.ps1 -Version 0.3.0
.EXAMPLE
    .\deploy-release.ps1 -Local -Publish
#>
param(
    [string]$Version,
    [switch]$Local,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

$Props = Join-Path $RepoRoot "Directory.Build.props"
$Csproj = Join-Path $RepoRoot "src\WslcAgent.App\WslcAgent.App.csproj"

# 1. What this checkout holds goes into the release: the release is made on
# main, and the changes not committed yet are carried there and committed,
# with no message to write.
$branch = (git branch --show-current)
if ($branch -ne "main") {
    $ahead = [int](git rev-list --count "main..HEAD")
    if ($ahead -gt 0) {
        throw "The branch $branch has $ahead commit(s) main does not; push it and open a pull request (this script then offers it), or merge it into main, and run this again."
    }
    git switch main
    if ($LASTEXITCODE -ne 0) { throw "Could not switch to main with these changes; commit or put them aside and run this again." }
}

if (@(git status --porcelain).Count -gt 0) {
    Write-Host "Changes not committed yet, which this release takes:" -ForegroundColor Cyan
    git status --short --untracked-files=no
    git add --update
    $new = @(git ls-files --others --exclude-standard)
    if ($new.Count -gt 0) {
        Write-Host "New files, not in the repository yet:" -ForegroundColor Cyan
        $new | ForEach-Object { Write-Host "  $_" }
        $answer = Read-Host "Take these new files into the release too? (y/n)"
        if ($answer -match '^(y|yes|s|si)$') { git add -- $new }
    }
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        $changed = @(git diff --cached --name-only)
        $summary = ($changed | Select-Object -First 4) -join ", "
        if ($changed.Count -gt 4) { $summary += " and $($changed.Count - 4) more" }
        git commit -m "Changes released from this checkout: $summary"
        if ($LASTEXITCODE -ne 0) { throw "git commit failed with exit code $LASTEXITCODE" }
    }
}

# 2. The release is made from main as GitHub has it, with the open pull
# requests the person chooses merged into it first. What was committed here
# goes on top of it.
function Sync-Main {
    git pull --rebase
    if ($LASTEXITCODE -ne 0) { throw "main could not be brought up to date (git pull --rebase); sort it out and run this again." }
}

Sync-Main
$open = @(gh pr list --base main --state open --json number,title --jq '.[] | "\(.number)\t\(.title)"')
if ($LASTEXITCODE -ne 0) { throw "gh could not list the open pull requests; is it signed in (gh auth status)?" }
foreach ($line in $open) {
    $number, $title = $line -split "`t", 2
    Write-Host ""
    Write-Host "Pull request #$number - $title" -ForegroundColor Cyan
    Write-Host "Waiting for its checks..."
    gh pr checks $number --watch --fail-fast | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Its checks did not pass: it is left out of this release." -ForegroundColor Yellow
        continue
    }
    $answer = Read-Host "Its checks passed. Merge #$number into this release? (y/n)"
    if ($answer -notmatch '^(y|yes|s|si)$') { continue }
    gh pr merge $number --squash --delete-branch
    if ($LASTEXITCODE -ne 0) { throw "#$number could not be merged; merge it on GitHub and run this again." }
}
if ($open.Count -gt 0) { Sync-Main }

# 3. The new versions, above the release's and every local build's.
$builtHere = Read-WslcAgentLocalVersions
$agentNow = Get-WslcAgentHigherVersion (Get-WslcAgentTrackedValue $Props "Version") $builtHere["WslcLocalAgentVersion"]
$clientNow = Get-WslcAgentHigherVersion (Get-WslcAgentTrackedValue $Csproj "ApplicationDisplayVersion") $builtHere["WslcLocalClientVersion"]
$localBuild = if ($builtHere["WslcLocalClientBuild"]) { [int]$builtHere["WslcLocalClientBuild"] } else { 0 }
$codeNow = [Math]::Max([int](Get-WslcAgentTrackedValue $Csproj "ApplicationVersion"), $localBuild)

if ($Version) {
    $release = ConvertTo-WixProductVersion $Version
    foreach ($current in @($agentNow, $clientNow)) {
        if ([version]$release -le [version](ConvertTo-WixProductVersion $current)) {
            throw "-Version $release is not above $current, the version already built; choose a higher one."
        }
    }
    $agentVersion = $release
    $clientVersion = $release
} else {
    $agentVersion = Get-NextPatchVersion $agentNow
    $clientVersion = Get-NextPatchVersion $clientNow
}
$clientCode = $codeNow + 1
$tag = "v$agentVersion"
if (git tag --list $tag) {
    throw "The tag $tag exists already; choose another version with -Version."
}

Write-Host "Release: agent $agentNow -> $agentVersion, client $clientNow -> $clientVersion (versionCode $clientCode), tag $tag" -ForegroundColor Cyan
Set-WslcAgentTrackedValue $Props "Version" $agentVersion
Set-WslcAgentTrackedValue $Csproj "ApplicationDisplayVersion" $clientVersion
Set-WslcAgentTrackedValue $Csproj "ApplicationVersion" "$clientCode"

# 4. Built here only with -Local, at exactly those versions; any failure puts the files back.
if ($Local) {
    try {
        & (Join-Path $RepoRoot "build-agent-installer.ps1") -NoBump -Release
        & (Join-Path $RepoRoot "build-client-installer.ps1") -NoBump
        & (Join-Path $RepoRoot "build-client-apk.ps1") -NoBump
    } catch {
        git checkout -- $Props $Csproj
        throw "The release was not made, and the versions were put back: $($_.Exception.Message)"
    }
}

# 5. The versions committed, tagged and pushed. The tag of a release built here
# says so: the release workflow leaves it alone.
git add -- $Props $Csproj
git commit -m "Release $agentVersion (client $clientVersion, versionCode $clientCode)"
if ($LASTEXITCODE -ne 0) { throw "git commit failed with exit code $LASTEXITCODE" }
$tagMessage = if ($Local) { "Release $agentVersion (built locally)" } else { "Release $agentVersion" }
git tag -a $tag -m $tagMessage
if ($LASTEXITCODE -ne 0) { throw "git tag failed with exit code $LASTEXITCODE" }
git push
if ($LASTEXITCODE -ne 0) { throw "git push failed with exit code $LASTEXITCODE" }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push of $tag failed with exit code $LASTEXITCODE" }

if (-not $Local) {
    $repository = (gh repo view --json nameWithOwner --jq .nameWithOwner 2>$null)
    Write-Host ""
    Write-Host "Release $tag committed, tagged and pushed. GitHub Actions builds the installers and publishes the release (about 20 minutes):" -ForegroundColor Green
    Write-Host "  https://github.com/$repository/actions/workflows/release.yml"
    return
}

$installers = @("wslc-ai-agent.msi", "wslc-ai-client.msi", "wslc-ai-client.apk") | ForEach-Object { Join-Path $RepoRoot "dist\$_" }
# Beside them, the objects' defaults and the default dashboard the agent
# installer ships, each with its version (docs/dashboard-defaults.md).
$installers += Write-WslcAgentDefaultsFiles -Destination (Join-Path $RepoRoot "dist")

# 6. The GitHub release, when asked.
if ($Publish) {
    gh release create $tag @installers --title "WSLC AI Agent $agentVersion" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE; the tag $tag is pushed, create the release by hand." }
    Write-Host "Release $tag published with its three installers and the two defaults files." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Release $tag committed, tagged and pushed. Upload these to its GitHub release:" -ForegroundColor Green
    $installers | ForEach-Object { Write-Host "  $_" }
}
