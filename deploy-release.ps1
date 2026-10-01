<#
.SYNOPSIS
    Publish a release: raise the release version above every build so far,
    build the three installers at it, and commit, tag and push it.
.DESCRIPTION
    The version in the repository is the last release's (Directory.Build.props
    for the agent, the client's csproj for the client and its Android
    versionCode); everyday builds raise only this checkout's own, in
    private\version.props. A release raises the repository's above both:

      1. Stops when the working tree has uncommitted changes.
      2. Writes the new versions: the agent's and the client's next patch
         above the higher of the release's and private\version.props, or
         -Version for both; the versionCode one above the higher of the two.
      3. Builds the agent installer (-Release: it offers
         C:\Berpiztu\wslc-ai-agent as the package folder), the Windows client
         installer and the APK, all with -NoBump, so they carry exactly the
         release's version.
      4. Only when all three built: commits the two files, tags v<agent
         version> and pushes both. When a build fails, the two files are put
         back and nothing is committed.
      5. With -Publish, creates the GitHub release with gh and uploads the
         three installers and the two defaults files (the objects' defaults
         and the default dashboard, each with its version); without it, says
         which files to upload. Between releases, publish-defaults.ps1
         replaces those two alone.

    The APK is signed with private\android.keystore (see
    docs\developer\private-files.md): a release signed with another key cannot
    update the installed app.
.PARAMETER Version
    The release's version for the agent and the client, x.y.z; it has to be
    above the agent's and the client's current ones.
.PARAMETER Publish
    Also create the GitHub release (gh must be signed in) and upload the
    installers to it.
.EXAMPLE
    .\deploy-release.ps1
.EXAMPLE
    .\deploy-release.ps1 -Version 0.3.0 -Publish
#>
param(
    [string]$Version,
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

$Props = Join-Path $RepoRoot "Directory.Build.props"
$Csproj = Join-Path $RepoRoot "src\WslcAgent.App\WslcAgent.App.csproj"

# 1. A release commits exactly the versions: nothing else may ride with it.
$dirty = @(git status --porcelain --untracked-files=no)
if ($dirty.Count -gt 0) {
    throw "Commit or discard these changes first; a release commits its versions alone:`n$($dirty -join "`n")"
}

# 2. The new versions, above the release's and every local build's.
$local = Read-WslcAgentLocalVersions
$agentNow = Get-WslcAgentHigherVersion (Get-WslcAgentTrackedValue $Props "Version") $local["WslcLocalAgentVersion"]
$clientNow = Get-WslcAgentHigherVersion (Get-WslcAgentTrackedValue $Csproj "ApplicationDisplayVersion") $local["WslcLocalClientVersion"]
$localBuild = if ($local["WslcLocalClientBuild"]) { [int]$local["WslcLocalClientBuild"] } else { 0 }
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

# 3. The three installers at exactly those versions; any failure puts the files back.
try {
    & (Join-Path $RepoRoot "build-agent-installer.ps1") -NoBump -Release
    & (Join-Path $RepoRoot "build-client-installer.ps1") -NoBump
    & (Join-Path $RepoRoot "build-client-apk.ps1") -NoBump
} catch {
    git checkout -- $Props $Csproj
    throw "The release was not made, and the versions were put back: $($_.Exception.Message)"
}

# 4. The versions committed, tagged and pushed.
git add -- $Props $Csproj
git commit -m "Release $agentVersion (client $clientVersion, versionCode $clientCode)"
if ($LASTEXITCODE -ne 0) { throw "git commit failed with exit code $LASTEXITCODE" }
git tag -a $tag -m "Release $agentVersion"
if ($LASTEXITCODE -ne 0) { throw "git tag failed with exit code $LASTEXITCODE" }
git push
if ($LASTEXITCODE -ne 0) { throw "git push failed with exit code $LASTEXITCODE" }
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "git push of $tag failed with exit code $LASTEXITCODE" }

$installers = @("wslc-ai-agent.msi", "wslc-ai-client.msi", "wslc-ai-client.apk") | ForEach-Object { Join-Path $RepoRoot "dist\$_" }
# Beside them, the objects' defaults and the default dashboard the agent
# installer ships, each with its version (docs/dashboard-defaults.md).
$installers += Write-WslcAgentDefaultsFiles -Destination (Join-Path $RepoRoot "dist")

# 5. The GitHub release, when asked.
if ($Publish) {
    gh release create $tag @installers --title "WSLC AI Agent $agentVersion" --generate-notes
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit code $LASTEXITCODE; the tag $tag is pushed, create the release by hand." }
    Write-Host "Release $tag published with its three installers and the two defaults files." -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Release $tag committed, tagged and pushed. Upload these to its GitHub release:" -ForegroundColor Green
    $installers | ForEach-Object { Write-Host "  $_" }
}
