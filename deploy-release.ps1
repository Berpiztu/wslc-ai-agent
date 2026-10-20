<#
.SYNOPSIS
    Publish a release: raise the release version above every build so far,
    and commit, tag and push it; GitHub Actions builds the installers and
    publishes the release (.github/workflows/release.yml). -Local builds and
    uploads them from this machine instead.
.DESCRIPTION
    The version in the repository is the last release's (Directory.Build.props
    for the agent, the client's csproj for the client and its Android
    versionCode); everyday builds only count themselves, as a fourth part of
    it in private\version.props. A release raises the repository's:

      1. Takes what this checkout holds. On main the release is an official
         one, made on main; on develop, or any other branch, it is a preview,
         made on develop and published as a pre-release, which the agents
         installed from an official release never offer. From another branch
         it goes to develop (one with commits of its own has to be pushed and
         in a pull request, open or merged, first). It commits the changes
         not committed yet, with a message it writes itself; new files are
         listed and taken only when the person says so.
      2. Brings that branch up to date (rebasing that commit on top). Then,
         for each open pull request into it, waits for its
         checks and asks whether to merge it (squash); one whose checks fail
         is reported and left alone. The branch is brought up to date again
         with what was merged, so the release carries it.
      3. Writes the new versions: a patch, the next fourth part after the
         last release and every local build of it (local builds and patches
         share that counter: 1.6.1.3 built here makes the patch 1.6.1.4), or
         -Version for both; the versionCode is the client's version as one
         number (1.6.1.4 is 106001004).
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
    The release's version for the agent and the client, x.y.z or x.y.z.w:
    a preview of WSL takes its patch number as the third part (WSL 3.0.4
    makes 1.6.4), WSL going official the next minor (1.7.0). It has to be
    above the last release and every local build made here.
.PARAMETER Local
    Build the installers here and leave GitHub Actions out of it.
.PARAMETER Publish
    With -Local, also create the GitHub release (gh must be signed in) and
    upload the installers to it. Without -Local the workflow always does.
.EXAMPLE
    .\deploy-release.ps1
.EXAMPLE
    .\deploy-release.ps1 -Version 1.6.4
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

# Run on main, the release is an official one, made on main; run on develop,
# or on any other branch, it is a preview, made on develop and published as a
# pre-release.
$branch = (git branch --show-current)
$target = if ($branch -eq "main") { "main" } else { "develop" }
$preview = $target -eq "develop"

# The last official release, as main holds it on GitHub.
function Get-OfficialVersion {
    git fetch --quiet origin main 2>$null
    $text = (git show origin/main:Directory.Build.props) -join "`n"
    if ($text -notmatch '<Version>([^<]+)</Version>') { throw "Could not read main's version (origin/main:Directory.Build.props)." }
    return $Matches[1].Trim()
}

# The release's versions. A patch by default: the next fourth part after the
# last release and every local build of it (1.6.1.3 built here, so 1.6.1.4),
# the counter they share. -Version for anything else: a preview (1.6.4) or a
# minor (1.7.0).
function Get-ReleaseVersions {
    $agentNow = Get-WslcAgentTrackedValue $Props "Version"
    $clientNow = Get-WslcAgentTrackedValue $Csproj "ApplicationDisplayVersion"
    $local = Read-WslcAgentLocalVersions
    $agentNext = (Get-WslcAgentLocalVersion $agentNow $local["WslcLocalAgentVersion"]).Version
    $clientNext = (Get-WslcAgentLocalVersion $clientNow $local["WslcLocalClientVersion"]).Version
    if (-not $Version) {
        # A patch of a preview is a patch of develop's own release; while that
        # is still the official one, a patch would carry its number into the
        # previews (1.6.1.3), so the first preview names WSL's (1.6.4).
        if ($preview -and (ConvertTo-WslcAgentComparableVersion $agentNow) -eq (ConvertTo-WslcAgentComparableVersion (Get-OfficialVersion))) {
            throw "develop is still at $agentNow, the official release: the first preview after it takes the WSL preview's patch number, .\deploy-release.ps1 -Version x.y.N (WSL 3.0.4 makes 1.6.4). Its patches then need no -Version. Nothing was committed, merged or pushed."
        }
        return [pscustomobject]@{ AgentNow = $agentNow; ClientNow = $clientNow; Agent = $agentNext; Client = $clientNext }
    }

    if ($Version.Trim() -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
        throw "-Version $Version is not x.y.z or x.y.z.w."
    }
    $release = ConvertTo-WslcAgentVersionText $Version
    # Above the last release and every build made here: one below a build
    # would not be taken as an update by the agent that build installed.
    foreach ($lowest in @($agentNext, $clientNext)) {
        if ((ConvertTo-WslcAgentComparableVersion $release) -lt (ConvertTo-WslcAgentComparableVersion $lowest)) {
            throw "-Version $release is below $lowest, the next version after the last release and the builds made here; choose a higher one."
        }
    }
    return [pscustomobject]@{ AgentNow = $agentNow; ClientNow = $clientNow; Agent = $release; Client = $release }
}

# 0. Every release has its notes, so nothing is done without them: no
# commit, no merge, no push. Looked for here, before anything else, and again
# once the branch is up to date, when they must be in the repository too.
$planned = (Get-ReleaseVersions).Agent
if (-not (Get-WslcAgentReleaseNotes "v$planned")) {
    throw "docs\release-notes\$planned.md is not there: every release has its notes, all its minor version brings with what is new. Write them and run this again; nothing was committed, merged or pushed."
}
Write-Host "Release notes found: docs\release-notes\$planned.md" -ForegroundColor Cyan

# And no release of a branch whose libraries security-watch says are vulnerable.
Test-WslcAgentSecurity -Ref $target

# 1. What this checkout holds goes into the release, made on its branch. The
# changes not committed yet are carried there and committed, with no message
# to write.
if ($branch -ne $target) {
    $ahead = [int](git rev-list --count "$target..HEAD")
    if ($ahead -gt 0) {
        # Commits of its own are safe to leave behind once GitHub has them in a pull
        # request: an open one is offered below, a merged one comes with develop.
        # A merged one may have had its branch deleted by GitHub; an open one has
        # to hold every commit made here.
        $state = gh pr view $branch --json state --jq .state 2>$null
        if ($state -eq "OPEN") {
            git fetch --quiet origin $branch 2>$null
            if ($LASTEXITCODE -ne 0 -or [int](git rev-list --count "origin/$branch..HEAD") -gt 0) { $state = "" }
        }
        if ($state -notin @("OPEN", "MERGED")) {
            throw "The branch $branch has $ahead commit(s) $target does not, and no pull request on GitHub holds them; push it and open a pull request (this script then offers it), or merge it into $target, and run this again."
        }
        Write-Host "The branch $branch is pull request ($state) on GitHub: the release is made on $target." -ForegroundColor Cyan
    }
    git switch $target
    if ($LASTEXITCODE -ne 0) { throw "Could not switch to $target with these changes; commit or put them aside and run this again." }
}
if ($preview) {
    Write-Host "On develop: this release is a preview, published as a pre-release." -ForegroundColor Cyan
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

# 2. The release is made from its branch as GitHub has it, with the open pull
# requests the person chooses merged into it first. What was committed here
# goes on top of it.
function Sync-Target {
    git pull --rebase origin $target
    if ($LASTEXITCODE -ne 0) { throw "$target could not be brought up to date (git pull --rebase); sort it out and run this again." }
}

Sync-Target
$open = @(gh pr list --base $target --state open --json number,title --jq '.[] | "\(.number)\t\(.title)"')
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
if ($open.Count -gt 0) { Sync-Target }

# 3. The new versions, as the branch now holds them.
$versions = Get-ReleaseVersions
$agentNow = $versions.AgentNow
$clientNow = $versions.ClientNow
$agentVersion = $versions.Agent
$clientVersion = $versions.Client
$codeNow = [int](Get-WslcAgentTrackedValue $Csproj "ApplicationVersion")
$clientCode = Get-WslcAgentVersionCode $clientVersion
if ($clientCode -le $codeNow) {
    throw "The versionCode $clientCode of $clientVersion is not above the last release's, $codeNow."
}
$tag = "v$agentVersion"
if (git tag --list $tag) {
    throw "The tag $tag exists already; choose another version with -Version."
}

$kind = if ($preview) { "Preview" } else { "Release" }
Write-Host "${kind}: agent $agentNow -> $agentVersion, client $clientNow -> $clientVersion (versionCode $clientCode), tag $tag" -ForegroundColor Cyan
# What the release's page says of it: docs\release-notes\<version>.md, which
# every release has, a patch's holding all its minor brought as well.
$notes = @(Get-WslcAgentReleaseNotes $tag)
if ($notes.Count -eq 0) {
    throw "docs\release-notes\$agentVersion.md is not there: every release has its notes. Write them, commit them and run this again."
}
if (-not (git ls-files -- $notes[1])) {
    # GitHub builds the release from the tag's commit: a file that is only here would not be on its page.
    throw "$($notes[1]) is not in the repository; commit it and run this again."
}
Write-Host "Its page on GitHub opens with $($notes[1])." -ForegroundColor Cyan
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
# A preview's tag message starts with "Preview": the workflow publishes it as a pre-release.
git add -- $Props $Csproj
git commit -m "$kind $agentVersion (client $clientVersion, versionCode $clientCode)"
if ($LASTEXITCODE -ne 0) { throw "git commit failed with exit code $LASTEXITCODE" }
$tagMessage = if ($Local) { "$kind $agentVersion (built locally)" } else { "$kind $agentVersion" }
git tag -a $tag -m $tagMessage
if ($LASTEXITCODE -ne 0) { throw "git tag failed with exit code $LASTEXITCODE" }
git push origin $target
if ($LASTEXITCODE -ne 0) { throw "git push failed with exit code $LASTEXITCODE" }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push of $tag failed with exit code $LASTEXITCODE" }

# A release built on GitHub sends its list of libraries from there; one built here sends it now.
if ($Local) { Send-WslcAgentInventory -Ref ([pscustomobject]@{ Name = $tag; Type = "release" }) }

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
    $prerelease = @(if ($preview) { "--prerelease" })
    gh release create $tag @installers --title "WSLC AI Agent $agentVersion" --generate-notes @notes @prerelease
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE; the tag $tag is pushed, create the release by hand." }
    Write-Host "Release $tag published with its three installers and the two defaults files." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Release $tag committed, tagged and pushed. Upload these to its GitHub release:" -ForegroundColor Green
    $installers | ForEach-Object { Write-Host "  $_" }
}
