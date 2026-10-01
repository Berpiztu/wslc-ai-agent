<#
.SYNOPSIS
    Helpers shared by the build-*.ps1 packaging scripts. Dot-source it.
.DESCRIPTION
    Version bumps (agent: Directory.Build.props; client: the MAUI csproj),
    WiX file-list generation from a published payload, MSI Explorer version
    stamping, third-party notices, and Android signing key resolution.
#>

Set-StrictMode -Version Latest

function Get-WslcAgentRepoRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

# ---------------------------------------------------------------- versions --

function Get-NextPatchVersion {
    param([Parameter(Mandatory = $true)][string]$Version)
    $parts = @()
    foreach ($piece in ($Version.Trim() -split '\.')) {
        if ($piece -ne "") { $parts += [int]$piece }
    }
    while ($parts.Count -lt 3) { $parts += 0 }
    $parts[2] = [int]$parts[2] + 1
    return ($parts[0..2] -join '.')
}

function Get-WslcAgentReleasePart {
    <# A version's first three parts, major.minor.patch: the release it is or counts from. #>
    param([Parameter(Mandatory = $true)][string]$Version)
    $parts = @()
    foreach ($piece in ($Version.Trim() -split '\.')) {
        if ($piece -ne "") { $parts += $piece }
    }
    while ($parts.Count -lt 3) { $parts += "0" }
    return ($parts[0..2] -join '.')
}

function ConvertTo-WixProductVersion {
    <#
    MSI ProductVersion: major.minor.build, and a local build's fourth part.
    Windows Installer compares the first three only (the installers replace
    an equal one); the agent and the clients read all four to tell a newer
    local build.
    #>
    param([Parameter(Mandatory = $true)][string]$Display)
    $parts = @()
    foreach ($piece in ($Display.Trim() -split '\.')) {
        if ($piece -ne "") { $parts += $piece }
    }
    while ($parts.Count -lt 3) { $parts += "0" }
    return ($parts[0..([Math]::Min($parts.Count, 4) - 1)] -join '.')
}

function Get-WslcAgentVersionCode {
    <#
    The Android versionCode of a release: its version as one number
    (1.0.18 is 100018000), leaving a local build counted from it the
    thousand that follow, so every later release is above all of them.
    #>
    param([Parameter(Mandatory = $true)][string]$Release)
    $parts = @((Get-WslcAgentReleasePart $Release) -split '\.' | ForEach-Object { [int]$_ })
    return $parts[0] * 100000000 + $parts[1] * 1000000 + $parts[2] * 1000
}

function Get-WslcAgentTrackedValue {
    <# The text of one element in a tracked project file: the release's version, which no build changes. #>
    param([string]$File, [string]$Element)
    $text = [System.IO.File]::ReadAllText($File)
    if ($text -notmatch "<$Element>([^<]+)</$Element>") {
        throw "No <$Element> in $File"
    }
    return $Matches[1].Trim()
}

function Set-WslcAgentTrackedValue {
    <# Writes one element of a tracked project file: only deploy-release.ps1 does, when a release is published. #>
    param([string]$File, [string]$Element, [string]$Value)
    $text = [System.IO.File]::ReadAllText($File)
    if ($text -notmatch "<$Element>([^<]+)</$Element>") {
        throw "No <$Element> in $File"
    }
    # The bare element only: the csproj's line that takes the local version
    # carries a Condition, so it is never this one.
    $text = ([regex]"<$Element>[^<]+</$Element>").Replace($text, "<$Element>$Value</$Element>", 1)
    [System.IO.File]::WriteAllText($File, $text, (New-Object System.Text.UTF8Encoding $false))
}

function Read-WslcAgentLocalVersions {
    <#
    The versions this checkout's builds have reached, from
    private\version.props: never tracked, so building installers changes no
    tracked file, whoever builds. Directory.Build.props and the client's
    csproj take them when they are above the release's.
    #>
    $file = Join-Path (Get-WslcAgentPrivateFolder) "version.props"
    $values = @{}
    if (Test-Path -LiteralPath $file) {
        $text = [System.IO.File]::ReadAllText($file)
        foreach ($name in @("WslcLocalAgentVersion", "WslcLocalClientVersion", "WslcLocalClientBuild")) {
            if ($text -match "<$name>([^<]+)</$name>") { $values[$name] = $Matches[1].Trim() }
        }
    }
    return $values
}

function Write-WslcAgentLocalVersions {
    param([hashtable]$Values)
    $file = Join-Path (Get-WslcAgentPrivateFolder) "version.props"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
    $lines = @(
        "<Project>",
        "  <!-- The versions this checkout's builds have reached, written by the",
        "       packaging scripts; never tracked. Taken over the release's when above it. -->",
        "  <PropertyGroup>"
    )
    foreach ($name in @("WslcLocalAgentVersion", "WslcLocalClientVersion", "WslcLocalClientBuild")) {
        if ($Values.ContainsKey($name)) { $lines += "    <$name>$($Values[$name])</$name>" }
    }
    $lines += @("  </PropertyGroup>", "</Project>", "")
    [System.IO.File]::WriteAllText($file, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding $false))
}

function Get-WslcAgentLocalBuildNumber {
    <#
    How many local builds this checkout has made of the release it is at: the
    fourth part of its local version when that counts from this release; 0
    when there is none, or it counts from an earlier release (a release
    starts the count again).
    #>
    param([string]$Release, [string]$Local)
    if (-not $Local) { return 0 }
    $parts = @($Local.Trim() -split '\.')
    if ($parts.Count -ne 4 -or (Get-WslcAgentReleasePart $Local) -ne (Get-WslcAgentReleasePart $Release)) { return 0 }
    return [int]$parts[3]
}

function Get-WslcAgentLocalVersion {
    <#
    A build's version: the release's, with the local build's number as a
    fourth part (1.0.27.3), so a build never takes a release's number and
    the next release, 1.0.28, is above every build of 1.0.27. -NoBump keeps
    the number of the last build; otherwise it is the next one.
    #>
    param([string]$Release, [string]$Local, [switch]$NoBump)
    $built = Get-WslcAgentLocalBuildNumber $Release $Local
    $number = if ($NoBump) { $built } else { $built + 1 }
    $base = Get-WslcAgentReleasePart $Release
    $version = if ($number -gt 0) { "$base.$number" } else { $base }
    return [pscustomobject]@{ Version = $version; Number = $number }
}

function Update-WslcAgentVersion {
    <#
    Agent version: the release's <Version> in Directory.Build.props with this
    checkout's build number as a fourth part, written to private\version.props
    only. A release, and only a release, raises the version itself.
    #>
    param([switch]$NoBump)
    $tracked = Get-WslcAgentTrackedValue (Join-Path (Get-WslcAgentRepoRoot) "Directory.Build.props") "Version"
    $local = Read-WslcAgentLocalVersions
    $build = Get-WslcAgentLocalVersion $tracked $local["WslcLocalAgentVersion"] -NoBump:$NoBump
    if ($NoBump) { return $build.Version }
    $local["WslcLocalAgentVersion"] = $build.Version
    Write-WslcAgentLocalVersions $local
    Write-Host "Agent build $($build.Version): local build $($build.Number) of release $tracked (private\version.props)" -ForegroundColor Cyan
    return $build.Version
}

function Update-WslcAgentClientVersion {
    <#
    Client version: the release's ApplicationDisplayVersion in the MAUI
    csproj with this checkout's build number as a fourth part, and the
    Android versionCode the release's code plus that number (1.0.17's third
    build is 100017003), written to private\version.props only.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Csproj,
        [switch]$NoBump
    )
    $tracked = Get-WslcAgentTrackedValue $Csproj "ApplicationDisplayVersion"
    $local = Read-WslcAgentLocalVersions
    $build = Get-WslcAgentLocalVersion $tracked $local["WslcLocalClientVersion"] -NoBump:$NoBump
    $code = if ($build.Number -gt 0) {
        (Get-WslcAgentVersionCode $tracked) + $build.Number
    } else {
        [int](Get-WslcAgentTrackedValue $Csproj "ApplicationVersion")
    }
    if (-not $NoBump) {
        $local["WslcLocalClientVersion"] = $build.Version
        $local["WslcLocalClientBuild"] = "$code"
        Write-WslcAgentLocalVersions $local
        Write-Host "Client build $($build.Version) (versionCode $code): local build $($build.Number) of release $tracked (private\version.props)" -ForegroundColor Cyan
    }
    return [pscustomobject]@{ Display = $build.Version; Build = $code }
}


# --------------------------------------------------------------- payloads --

function Copy-WslcAgentNotices {
    <# LICENSE and the third-party notices ship with every binary. #>
    param([Parameter(Mandatory = $true)][string]$Destination)
    $root = Get-WslcAgentRepoRoot
    $licenses = Join-Path $Destination "licenses"
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $root "LICENSE") -Destination (Join-Path $Destination "LICENSE.txt") -Force
    Copy-Item -LiteralPath (Join-Path $root "THIRD-PARTY-NOTICES.md") -Destination $Destination -Force
    # Every full license text the notices point to (Apache-2.0, OFL-1.1).
    Get-ChildItem -LiteralPath (Join-Path $root "packaging\licenses") -File | Copy-Item -Destination $licenses -Force
}

function Write-WslcAgentDefaultsFiles {
    <#
    The objects' defaults and the default dashboard as they travel beside the
    installers (docs/dashboard-defaults.md): each repository file wrapped with
    its version and the oldest agent that loads it, from
    src\WslcAgent.Server\Overview\defaults-versions.json. Written into
    Destination; returns their paths.
    #>
    param([Parameter(Mandatory = $true)][string]$Destination)
    $overview = Join-Path (Get-WslcAgentRepoRoot) "src\WslcAgent.Server\Overview"
    $versions = [IO.File]::ReadAllText((Join-Path $overview "defaults-versions.json")) | ConvertFrom-Json
    $created = (Get-Date).ToString("yyyy-MM-ddTHH:mm:sszzz")
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    $files = @(
        @{ Kind = "object-defaults"; Entry = $versions.objectDefaults; Source = "object-defaults.json"; Name = "wslc-object-defaults.json" },
        @{ Kind = "dashboard"; Entry = $versions.dashboard; Source = "dashboard-v2.5.default.json"; Name = "wslc-dashboard-default.json" }
    )
    foreach ($file in $files) {
        # The content goes in as it is, untouched: ConvertTo-Json would reorder
        # and re-escape what the agent and the clients read.
        $content = [IO.File]::ReadAllText((Join-Path $overview $file.Source)).Trim()
        $text = "{`n" +
            "  ""kind"": ""wslc-$($file.Kind)"",`n" +
            "  ""version"": $([int]$file.Entry.version),`n" +
            "  ""minAgentVersion"": ""$($file.Entry.minAgentVersion)"",`n" +
            "  ""from"": """",`n" +
            "  ""created"": ""$created"",`n" +
            "  ""content"": $content`n}`n"
        $path = Join-Path $Destination $file.Name
        [IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $false))
        $path
    }
}

function Clear-WslcAgentIconCache {
    <#
    MAUI's resizetizer caches generated icons and splash images under
    obj\**\resizetizer and does not always notice a change in the MauiIcon
    metadata (e.g. removing Color left opaque corners in the Windows .ico).
    Regenerating costs seconds, so every client build starts clean.
    #>
    $appObj = Join-Path (Get-WslcAgentRepoRoot) "src\WslcAgent.App\obj"
    Get-ChildItem -Path $appObj -Directory -Recurse -Filter resizetizer -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -Recurse -Force -LiteralPath $_.FullName }
}

function Remove-WslcAgentDebugFiles {
    param([Parameter(Mandatory = $true)][string]$Path)
    Get-ChildItem -LiteralPath $Path -Recurse -Filter *.pdb -ErrorAction SilentlyContinue |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}

# -------------------------------------------------------------------- WiX --

function ConvertTo-WixId([string]$Prefix, [string]$Value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))
    } finally {
        $sha.Dispose()
    }
    return $Prefix + (-join ($bytes[0..9] | ForEach-Object { $_.ToString("x2") }))
}

function ConvertTo-XmlText([string]$Value) {
    return ($Value -replace "&", "&amp;" -replace "<", "&lt;" -replace ">", "&gt;" -replace '"', "&quot;")
}

function Write-WslcAgentWixFileList {
    <# Emit a WiX fragment that installs every file under $Stage into INSTALLFOLDER. #>
    param(
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$OutFile,
        [string]$ComponentGroupId = "PublishedFiles"
    )
    $stageFull = (Resolve-Path -LiteralPath $Stage).Path.TrimEnd('\')
    $files = @(Get-ChildItem -LiteralPath $stageFull -Recurse -File)
    if ($files.Count -eq 0) { throw "No files to package under $stageFull" }

    $dirIds = @{ "" = "INSTALLFOLDER" }
    $relDirs = New-Object "System.Collections.Generic.HashSet[string]"
    foreach ($file in $files) {
        $rel = $file.FullName.Substring($stageFull.Length).TrimStart('\', '/')
        $dirRel = Split-Path -Parent $rel
        if (-not $dirRel) { continue }
        $acc = ""
        foreach ($part in ($dirRel -split '[\\/]')) {
            $acc = if ($acc) { "$acc\$part" } else { $part }
            [void]$relDirs.Add($acc)
            if (-not $dirIds.ContainsKey($acc)) { $dirIds[$acc] = ConvertTo-WixId "d" $acc }
        }
    }
    $children = @{}
    foreach ($rel in $relDirs) {
        $parent = Split-Path -Parent $rel
        if (-not $parent) { $parent = "" }
        if (-not $children.ContainsKey($parent)) { $children[$parent] = New-Object System.Collections.Generic.List[string] }
        $children[$parent].Add($rel)
    }
    $sb = New-Object System.Text.StringBuilder
    function Write-DirTree([string]$parentRel, [int]$indent) {
        if (-not $children.ContainsKey($parentRel)) { return }
        $pad = " " * $indent
        foreach ($rel in ($children[$parentRel] | Sort-Object)) {
            [void]$sb.AppendLine("$pad<Directory Id=`"$($dirIds[$rel])`" Name=`"$(ConvertTo-XmlText (Split-Path -Leaf $rel))`">")
            Write-DirTree $rel ($indent + 2)
            [void]$sb.AppendLine("$pad</Directory>")
        }
    }
    [void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    [void]$sb.AppendLine('  <!-- Generated by packaging\Packaging.ps1 from the published payload. Do not edit. -->')
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')
    Write-DirTree "" 6
    [void]$sb.AppendLine('    </DirectoryRef>')
    [void]$sb.AppendLine("    <ComponentGroup Id=`"$ComponentGroupId`">")
    foreach ($file in ($files | Sort-Object FullName)) {
        $rel = $file.FullName.Substring($stageFull.Length).TrimStart('\', '/')
        $dirRel = Split-Path -Parent $rel
        if (-not $dirRel) { $dirRel = "" }
        # DefaultLanguage only matters for versioned files without a language
        # resource; on unversioned files WiX warns that it is ignored (WIX1102).
        # The extension does not say which is which — the WinAppSDK ships native
        # DLLs with no version resource at all (marshal.dll,
        # Microsoft.UI.Composition.OSSupport.dll, Microsoft.UI.Windowing.dll) —
        # so the file itself is asked.
        $lang = if (Test-WslcAgentVersionedFile $file.FullName) { ' DefaultLanguage="0"' } else { "" }
        [void]$sb.AppendLine("      <Component Id=`"$(ConvertTo-WixId 'c' $rel)`" Directory=`"$($dirIds[$dirRel])`" Guid=`"*`">")
        [void]$sb.AppendLine("        <File Id=`"$(ConvertTo-WixId 'f' $rel)`" Source=`"$(ConvertTo-XmlText $file.FullName)`" KeyPath=`"yes`"$lang />")
        [void]$sb.AppendLine("      </Component>")
    }
    [void]$sb.AppendLine('    </ComponentGroup>')
    [void]$sb.AppendLine('  </Fragment>')
    [void]$sb.AppendLine('</Wix>')
    [System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Wrote $OutFile ($($files.Count) files)" -ForegroundColor DarkGray
}

function Test-WslcAgentVersionedFile {
    <# True when the file carries a version resource, which is what makes MSI
       treat it as versioned and DefaultLanguage meaningful (WIX1102). #>
    param([Parameter(Mandatory = $true)][string]$Path)
    try {
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
        return -not [string]::IsNullOrEmpty($info.FileVersion)
    }
    catch {
        # Not a binary Windows can read a version out of: unversioned, then.
        return $false
    }
}

function Build-WslcAgentMsi {
    <#
    Build a WiX project and return the path of the produced .msi.
    -Properties are MSBuild properties the project hands to WiX as defines
    (the agent's package folder, the client's encoded uninstall script); the
    project's own defaults stand for any not given.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$WixProj,
        [Parameter(Mandatory = $true)][string]$Version,
        [hashtable]$Properties = @{}
    )
    $outDir = Join-Path (Split-Path -Parent $WixProj) "bin\Release"
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    # @( ) around the whole: a pipeline that yields one item yields a string,
    # and a string splatted goes one character per argument.
    $extra = @($Properties.GetEnumerator() | ForEach-Object { "-p:$($_.Key)=$($_.Value)" })
    # Out-Host keeps the build output off the pipeline: this function's only
    # return value must be the .msi path.
    & dotnet build $WixProj -c Release -nologo -v q -p:OutputPath="$outDir\" -p:MsiVersion=$Version @extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE" }
    $msi = Get-ChildItem -LiteralPath $outDir -Filter *.msi -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $msi) { throw "WiX build succeeded but no .msi was found under $outDir" }
    return $msi.FullName
}

function Set-WslcAgentMsiExplorerVersion {
    <# Stamp the MSI Summary Information so Explorer's Details tab shows the version. #>
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Version
    )
    $label = "$Name $Version"
    $full = (Resolve-Path -LiteralPath $Path).Path
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $sum = $null
    try {
        $sum = $installer.GetType().InvokeMember("SummaryInformation", [Reflection.BindingFlags]::GetProperty, $null, $installer, @($full, 20))
        foreach ($id in @(2, 3, 6, 18)) {
            $value = if ($id -eq 6) { $Version } else { $label }
            [void]$sum.GetType().InvokeMember("Property", [Reflection.BindingFlags]::SetProperty, $null, $sum, @([int]$id, [string]$value))
        }
        [void]$sum.GetType().InvokeMember("Persist", [Reflection.BindingFlags]::InvokeMethod, $null, $sum, @())
    } finally {
        if ($null -ne $sum) { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($sum) }
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($installer)
        [GC]::Collect()
        [GC]::WaitForPendingFinalizers()
    }
}

# ---------------------------------------------------------------- Android --

function Write-WslcAgentCheck {
    <#
    One line of a check script's report: what was checked, its state, and what
    it means or how to fix it. -Run is the fix itself: each command on a line
    of its own, so one copy takes one whole command (-Admin: it needs a
    PowerShell run as administrator). -Then is what has to follow it before
    anything sees what it installed, such as a new terminal, in yellow so it
    is not missed. When it is not ok, -Guide (a page of the
    repository, with its section) is printed under it: where the fix is
    explained step by step. Counts the broken ones in
    $script:WslcAgentCheckBroken of the script that called it, and keeps every
    fix in $script:WslcAgentCheckFixes, for install-prereqs.ps1 to run.
    #>
    param(
        [ValidateSet("ok", "absent", "broken")][string]$State,
        [string]$What,
        [string]$Detail,
        [string]$Guide,
        [string[]]$Run,
        [switch]$Admin,
        [string]$Then
    )
    $colour = @{ ok = "Green"; absent = "Yellow"; broken = "Red" }[$State]
    $indent = " " * 13
    if ($State -eq "broken") { $script:WslcAgentCheckBroken++ }
    Write-Host ("  {0,-9} " -f "[$State]") -ForegroundColor $colour -NoNewline
    Write-Host "$What  " -NoNewline
    Write-Host $Detail
    if ($Run) {
        if (Test-Path Variable:script:WslcAgentCheckFixes) {
            $script:WslcAgentCheckFixes += [pscustomobject]@{ What = $What; Run = $Run; Then = $Then }
        }
        Write-Host "${indent}run$(if ($Admin) { ', from a PowerShell opened as administrator' }):" -ForegroundColor DarkGray
        foreach ($command in $Run) { Write-Host "$indent  $command" -ForegroundColor Cyan }
    }
    # install-prereqs.ps1 does what -Then asks between its commands itself.
    if ($Then -and -not (Test-Path Variable:script:WslcAgentCheckInstalling)) {
        Write-Host "${indent}then: $Then" -ForegroundColor Yellow
    }
    if ($Guide -and $State -ne "ok") {
        Write-Host "${indent}how: $Guide" -ForegroundColor DarkGray
    }
}

function Find-WslcAgentAndroidSdk {
    # The Android SDK the MAUI workload uses: ANDROID_HOME, ANDROID_SDK_ROOT,
    # then where Visual Studio and Android Studio install it. Null when absent.
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "${env:ProgramFiles(x86)}\Android\android-sdk", "$env:LOCALAPPDATA\Android\Sdk")) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate "platform-tools\adb.exe"))) { return $candidate }
    }
    return $null
}

function Invoke-WslcAgentNative {
    <#
    Runs a native command and returns its standard output, its standard error
    dropped; $LASTEXITCODE holds its exit code. Windows PowerShell turns every
    line a native command writes to standard error into an error record once
    that stream is redirected, and under $ErrorActionPreference = "Stop" the
    first one ends the script: keytool, dotnet, wsl, adb and ssh all write
    progress or notices there. PowerShell 7 does not, which is how it goes
    unseen.
    #>
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $FilePath @Arguments 2>$null
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Get-WslcAgentSolutionScope {
    <#
    The MSBuild properties a build of the whole solution takes on this
    machine. The Android client needs the Android SDK and a JDK; without them
    the solution is built without its Android target, and says so, instead of
    failing: everything else still builds, runs and passes its tests.
    #>
    $keytool = try { Find-WslcAgentKeytool } catch { $null }
    if ((Find-WslcAgentAndroidSdk) -and $keytool) { return @() }
    Write-Host "No Android SDK or no JDK: building without the Android client (docs\developer\prerequisites.md#android-sdk)." -ForegroundColor Yellow
    return @("-p:WslcAgentWindowsOnly=true")
}

function Get-WslcAgentPrivateFolder {
    <#
    The checkout's private/ folder: the signing key, the Firebase files and
    env.psd1. Git tracks nothing in it but README.md and env.example.psd1
    (.gitignore); docs/developer/private-files.md says how to make each file.
    #>
    return Join-Path (Split-Path -Parent $PSScriptRoot) "private"
}

function Import-WslcAgentPrivateSettings {
    <#
    Loads private\env.psd1 into the environment, so a developer fills one file
    instead of setting variables by hand. A variable already set in the
    environment wins over the file, and an empty value in the file sets
    nothing. The file is a PowerShell data file: it is read, never run.
    #>
    $file = Join-Path (Get-WslcAgentPrivateFolder) "env.psd1"
    if (-not (Test-Path -LiteralPath $file)) { return }
    $settings = Import-PowerShellDataFile -LiteralPath $file
    foreach ($name in $settings.Keys) {
        $value = [string]$settings[$name]
        if ($value -and -not [Environment]::GetEnvironmentVariable($name)) {
            [Environment]::SetEnvironmentVariable($name, $value)
        }
    }
}

function Resolve-WslcAgentAndroidSigning {
    <#
    Android refuses to update an app whose new APK is signed with a different
    key, so every machine that builds one has to sign with the same key.
    Resolution order:
      1. WSLC_AGENT_KEYSTORE (+ WSLC_AGENT_KEYSTORE_PASS, WSLC_AGENT_KEY_ALIAS, WSLC_AGENT_KEY_PASS)
      2. private\android.keystore in this checkout, with its password in
         android.keystore.pass beside it
      3. %USERPROFILE%\.wslc-agent\android.keystore, the same pair of files,
         for a machine that keeps one key for several checkouts
    When none of them exists a new keystore is generated at (2). Back it up:
    an APK signed with any other key cannot update the installed app.
    #>
    $keystore = $env:WSLC_AGENT_KEYSTORE
    $storePass = $env:WSLC_AGENT_KEYSTORE_PASS
    $alias = if ($env:WSLC_AGENT_KEY_ALIAS) { $env:WSLC_AGENT_KEY_ALIAS } else { "wslc-agent" }
    $keyPass = $env:WSLC_AGENT_KEY_PASS

    if (-not $keystore) {
        $private = Join-Path (Get-WslcAgentPrivateFolder) "android.keystore"
        $perUser = Join-Path $env:USERPROFILE ".wslc-agent\android.keystore"
        $keystore = if (-not (Test-Path -LiteralPath $private) -and (Test-Path -LiteralPath $perUser)) { $perUser } else { $private }
        $passFile = "$keystore.pass"
        if (-not (Test-Path -LiteralPath $keystore)) {
            $keytool = Find-WslcAgentKeytool
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $keystore) | Out-Null
            $storePass = -join ((1..32) | ForEach-Object { [char](Get-Random -InputObject ([int[]](48..57 + 65..90 + 97..122))) })
            [System.IO.File]::WriteAllText($passFile, $storePass, (New-Object System.Text.UTF8Encoding $false))
            Write-Host "No Android signing key found; generating $keystore" -ForegroundColor Yellow
            Invoke-WslcAgentNative $keytool @("-genkeypair", "-v", "-keystore", $keystore, "-alias", $alias, "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000",
                "-storepass", $storePass, "-keypass", $storePass, "-dname", "CN=wslc-agent") | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "keytool failed with exit code $LASTEXITCODE" }
            Write-Host "Back up $keystore and its .pass file: every later APK has to be signed with this key to update the installed app." -ForegroundColor Yellow
        }
        if (-not $storePass) {
            if (-not (Test-Path -LiteralPath $passFile)) { throw "Keystore $keystore exists but $passFile is missing; set WSLC_AGENT_KEYSTORE_PASS." }
            $storePass = ([System.IO.File]::ReadAllText($passFile)).Trim()
        }
    } elseif (-not (Test-Path -LiteralPath $keystore)) {
        throw "WSLC_AGENT_KEYSTORE points to a missing file: $keystore"
    } elseif (-not $storePass) {
        throw "WSLC_AGENT_KEYSTORE is set; set WSLC_AGENT_KEYSTORE_PASS too."
    }
    if (-not $keyPass) { $keyPass = $storePass }
    return [pscustomobject]@{ Keystore = $keystore; StorePass = $storePass; Alias = $alias; KeyPass = $keyPass }
}

function Find-WslcAgentKeytool {
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += (Join-Path $env:JAVA_HOME "bin\keytool.exe") }
    $candidates += Get-ChildItem -Path "${env:ProgramFiles(x86)}\Android\openjdk", "$env:ProgramFiles\Microsoft\jdk-*", "$env:ProgramFiles\Android\jdk" -Filter keytool.exe -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
    $cmd = Get-Command keytool -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    throw "keytool.exe not found. Install a JDK (the Android workload's OpenJDK is enough) or set JAVA_HOME."
}
