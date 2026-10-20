<#
.SYNOPSIS
    Berpiztu/security-watch from this repository's builds: asked before every
    build, told after one. Dot-sourced by Packaging.ps1.
.DESCRIPTION
    security-watch checks the libraries Berpiztu's projects use, each one once
    for all of them, and reads no project: every project sends its own list
    (inventories/<project>/<ref>.json) and reads its own status
    (status/<project>.json). docs/developer/security-watch.md says how.

    Test-WslcAgentSecurity, first thing in a build: the status of the ref
    being built. A finding moderate or worse stops the build; a lower one is
    written as a warning. When security-watch cannot be reached, the person
    is asked whether to go on; a build on GitHub Actions stops.

    Send-WslcAgentInventory, once a build succeeded: the list of every library
    this checkout uses, sent only when it differs from the one security-watch
    has, and only for main, develop and release tags. It writes with
    SECURITY_WATCH_TOKEN (private\env.psd1, or a secret on GitHub), a token
    that writes to security-watch alone.
#>

$WslcAgentSecurityProject = "wslc-ai-agent"
$WslcAgentSecurityRepository = "Berpiztu/security-watch"

function Get-WslcAgentSecurityRef {
    <# The ref this checkout is: the release tag on a release's build on GitHub, the branch otherwise. #>
    if ($env:GITHUB_REF_TYPE -eq "tag") {
        return [pscustomobject]@{ Name = $env:GITHUB_REF_NAME; Type = "release" }
    }
    $branch = git -C (Get-WslcAgentRepoRoot) branch --show-current
    if (-not $branch) { $branch = $env:GITHUB_HEAD_REF }
    return [pscustomobject]@{ Name = "$branch"; Type = "branch" }
}

function Test-WslcAgentSecurity {
    <#
    Asks security-watch about the ref being built, or the one named, before
    anything is built: stops on a finding moderate or worse, warns on a lower
    one, and asks whether to go on when security-watch cannot be reached.
    #>
    param([string]$Ref)
    if (-not $Ref) { $Ref = (Get-WslcAgentSecurityRef).Name }
    $url = "https://raw.githubusercontent.com/$WslcAgentSecurityRepository/main/status/$WslcAgentSecurityProject.json"
    try {
        $status = Invoke-RestMethod -Uri $url -TimeoutSec 20
    } catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 404) {
            Write-Host "security-watch: no status for $WslcAgentSecurityProject yet; its list is sent after this build." -ForegroundColor Yellow
            return
        }
        $reason = $_.Exception.Message
        if ($env:GITHUB_ACTIONS -eq "true") {
            throw "security-watch could not be reached ($reason): the build stops, since nothing says its libraries are safe."
        }
        $answer = Read-Host "security-watch could not be reached ($reason). Build anyway, without knowing of a new vulnerability? (y/n)"
        if ($answer -notmatch '^(y|yes|s|si)$') { throw "Stopped: security-watch could not be reached ($reason)." }
        Write-Warning "security-watch could not be reached ($reason); built without asking it."
        return
    }

    $mine = @($status.refs) | Where-Object { $_.ref -eq $Ref } | Select-Object -First 1
    if (-not $mine) {
        Write-Host "security-watch: no list of $Ref yet (checked $($status.checked)); it is sent after this build." -ForegroundColor Yellow
        return
    }

    $findings = @($mine.findings)
    foreach ($finding in $findings | Where-Object { -not $_.blocking }) {
        Write-Warning "security-watch: $($finding.package) $($finding.version) ($($finding.ecosystem)), $($finding.severity): $($finding.advisory) $($finding.cve), fixed in $(if ($finding.fixedIn) { $finding.fixedIn } else { 'no version yet' })."
    }
    $blocking = @($findings | Where-Object { $_.blocking })
    if ($blocking.Count -gt 0) {
        $lines = $blocking | ForEach-Object { "  $($_.package) $($_.version) ($($_.ecosystem)), $($_.severity): $($_.advisory) $($_.cve), fixed in $(if ($_.fixedIn) { $_.fixedIn } else { 'no version yet' })" }
        throw "security-watch: $Ref uses $($blocking.Count) librar$(if ($blocking.Count -eq 1) { 'y' } else { 'ies' }) with a vulnerability moderate or worse:`n$($lines -join "`n")`nPatch it (docs/developer/releasing.md, A security patch) and build again."
    }
    Write-Host "security-watch: $Ref has no known vulnerability that stops a build (checked $($status.checked))." -ForegroundColor Green
}

function Get-WslcAgentInventory {
    <#
    Every library this checkout uses, at the version it resolves: the NuGet
    packages of every project restored here (project.assets.json, transitive
    ones included), the central versions of the ones not restored here (the
    MAUI client without its workloads), the pinned lines of every tracked
    requirements file, the .NET SDK and the container images.
    #>
    param([Parameter(Mandatory = $true)]$Ref)
    $root = Get-WslcAgentRepoRoot
    $tracked = @(git -C $root ls-files)
    $packages = @{}
    function Add-Package([string]$Ecosystem, [string]$Name, [string]$Version, [bool]$Direct) {
        $key = "$Ecosystem|$($Name.ToLowerInvariant())|$Version"
        if (-not $packages.ContainsKey($key)) {
            $packages[$key] = [ordered]@{ ecosystem = $Ecosystem; name = $Name; version = $Version; direct = $Direct }
        } elseif ($Direct) {
            $packages[$key].direct = $true
        }
    }

    $central = @{}
    foreach ($node in ([xml][IO.File]::ReadAllText((Join-Path $root "Directory.Packages.props"))).SelectNodes("//PackageVersion")) {
        $central[$node.Include.ToLowerInvariant()] = $node.Version
    }
    foreach ($project in $tracked | Where-Object { $_ -like "*.csproj" }) {
        $assets = Join-Path $root (Join-Path (Split-Path -Parent $project) "obj\project.assets.json")
        if (-not (Test-Path -LiteralPath $assets)) { continue }
        $libraries = ([IO.File]::ReadAllText($assets) | ConvertFrom-Json).libraries
        foreach ($library in $libraries.PSObject.Properties) {
            if ($library.Value.type -ne "package") { continue }
            $name, $version = $library.Name -split "/", 2
            Add-Package "NuGet" $name $version ($central.ContainsKey($name.ToLowerInvariant()) -and $central[$name.ToLowerInvariant()] -eq $version)
        }
    }
    foreach ($node in ([xml][IO.File]::ReadAllText((Join-Path $root "Directory.Packages.props"))).SelectNodes("//PackageVersion")) {
        Add-Package "NuGet" $node.Include $node.Version $true
    }

    foreach ($file in $tracked | Where-Object { (Split-Path -Leaf $_) -like "*requirements*.txt" }) {
        foreach ($line in [IO.File]::ReadAllLines((Join-Path $root $file))) {
            if ($line -match '^\s*([A-Za-z0-9_.\-]+)(\[[^\]]*\])?\s*==\s*([^\s;#]+)') { Add-Package "PyPI" $Matches[1] $Matches[3] $true }
        }
    }

    $images = [System.Collections.Generic.SortedSet[string]]::new()
    foreach ($file in $tracked | Where-Object { (Split-Path -Leaf $_) -like "Dockerfile*" }) {
        $stages = @{}
        foreach ($line in [IO.File]::ReadAllLines((Join-Path $root $file))) {
            if ($line -match '^\s*FROM\s+(?:--\S+\s+)*(\S+)(?:\s+AS\s+(\S+))?') {
                $image = $Matches[1]
                if ($Matches[2]) { $stages[$Matches[2].ToLowerInvariant()] = $true }
                if ($image -ne "scratch" -and $image -notmatch '\$' -and -not $stages.ContainsKey($image.ToLowerInvariant())) { [void]$images.Add($image) }
            }
        }
    }
    foreach ($file in $tracked | Where-Object { (Split-Path -Leaf $_) -match '^(docker-)?compose.*\.ya?ml$' }) {
        foreach ($line in [IO.File]::ReadAllLines((Join-Path $root $file))) {
            if ($line -match '^\s*image:\s*["'']?([^"''\s#]+)' -and $Matches[1] -notmatch '\$') { [void]$images.Add($Matches[1]) }
        }
    }

    $sdk = ([IO.File]::ReadAllText((Join-Path $root "global.json")) | ConvertFrom-Json).sdk.version
    return [ordered]@{
        project = $WslcAgentSecurityProject
        ref = $Ref.Name
        type = $Ref.Type
        commit = (git -C $root rev-parse --short HEAD)
        sent = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        packages = @($packages.Values | Sort-Object { $_.ecosystem }, { $_.name.ToLowerInvariant() }, { $_.version })
        dotnetSdk = $sdk
        images = @($images)
    }
}

function Send-WslcAgentInventory {
    <#
    Sends this checkout's list of libraries to security-watch, when it is not
    the list security-watch already has: for main, develop and release tags
    alone, a working branch being nobody's install.
    #>
    param($Ref)
    if (-not $Ref) { $Ref = Get-WslcAgentSecurityRef }
    if ($Ref.Type -ne "release" -and $Ref.Name -notin @("main", "develop")) {
        Write-Host "security-watch: lists are sent for main, develop and releases; $($Ref.Name) is not sent." -ForegroundColor DarkGray
        return
    }

    Import-WslcAgentPrivateSettings
    $inventory = Get-WslcAgentInventory -Ref $Ref
    $path = "inventories/$WslcAgentSecurityProject/$($Ref.Name).json"
    $api = "https://api.github.com/repos/$WslcAgentSecurityRepository/contents/$path"
    $headers = @{ Accept = "application/vnd.github+json"; "User-Agent" = "wslc-ai-agent" }
    if ($env:SECURITY_WATCH_TOKEN) { $headers.Authorization = "Bearer $env:SECURITY_WATCH_TOKEN" }

    # The same libraries, whenever and from whichever commit they were sent, are the same list.
    $sameness = { param($list) ConvertTo-Json -Compress -Depth 5 @{ packages = @($list.packages | ForEach-Object { "$($_.ecosystem)|$($_.name)|$($_.version)|$($_.direct)" }); dotnetSdk = $list.dotnetSdk; images = @($list.images) } }
    $sha = $null
    try {
        $current = Invoke-RestMethod -Uri $api -Headers $headers -TimeoutSec 20
        $sha = $current.sha
        $known = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($current.content -replace '\s', ''))) | ConvertFrom-Json
        if ((& $sameness $known) -eq (& $sameness $inventory)) {
            Write-Host "security-watch already has this list of $($Ref.Name): nothing sent." -ForegroundColor DarkGray
            return
        }
    } catch {
        if (-not ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 404)) {
            Write-Warning "security-watch could not be read ($($_.Exception.Message)): the list of $($Ref.Name) was not sent."
            return
        }
    }

    if (-not $env:SECURITY_WATCH_TOKEN) {
        Write-Warning "The list of $($Ref.Name) changed, and SECURITY_WATCH_TOKEN is not set (private\env.psd1): it was not sent to security-watch."
        return
    }
    $body = [ordered]@{
        message = "${WslcAgentSecurityProject}: libraries of $($Ref.Name) ($($inventory.commit))"
        content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((ConvertTo-Json -Depth 5 $inventory) + "`n"))
    }
    if ($sha) { $body.sha = $sha }
    try {
        Invoke-RestMethod -Method Put -Uri $api -Headers $headers -ContentType "application/json" -Body (ConvertTo-Json $body) -TimeoutSec 30 | Out-Null
        Write-Host "security-watch: the list of $($Ref.Name) was sent ($(@($inventory.packages).Count) libraries); it is checked within minutes." -ForegroundColor Green
    } catch {
        Write-Warning "security-watch: the list of $($Ref.Name) could not be sent ($($_.Exception.Message))."
    }
}
