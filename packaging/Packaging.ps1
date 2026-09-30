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

function ConvertTo-WixProductVersion {
    # MSI ProductVersion is major.minor.build, each part numeric.
    param([Parameter(Mandatory = $true)][string]$Display)
    $parts = @()
    foreach ($piece in ($Display.Trim() -split '\.')) {
        if ($piece -ne "") { $parts += $piece }
    }
    while ($parts.Count -lt 3) { $parts += "0" }
    return ($parts[0..2] -join '.')
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

function Get-WslcAgentHigherVersion {
    param([string]$Tracked, [string]$Local)
    if ($Local -and ([version](ConvertTo-WixProductVersion $Local)) -gt ([version](ConvertTo-WixProductVersion $Tracked))) { return $Local }
    return $Tracked
}

function Update-WslcAgentVersion {
    <#
    Agent version: the release's <Version> in Directory.Build.props, or this
    checkout's higher one in private\version.props; a bump raises the patch
    of whichever is higher and writes it to private\version.props only.
    #>
    param([switch]$NoBump)
    $tracked = Get-WslcAgentTrackedValue (Join-Path (Get-WslcAgentRepoRoot) "Directory.Build.props") "Version"
    $local = Read-WslcAgentLocalVersions
    $current = Get-WslcAgentHigherVersion $tracked $local["WslcLocalAgentVersion"]
    if ($NoBump) { return $current }
    $next = Get-NextPatchVersion $current
    $local["WslcLocalAgentVersion"] = $next
    Write-WslcAgentLocalVersions $local
    Write-Host "Bumped agent version $current -> $next (private\version.props)" -ForegroundColor Cyan
    return $next
}

function Update-WslcAgentClientVersion {
    <#
    Client version: ApplicationDisplayVersion and ApplicationVersion (the
    Android versionCode) of the release in the MAUI csproj, or this
    checkout's higher ones in private\version.props; a bump raises the patch
    and the code of whichever are higher and writes them there only.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Csproj,
        [switch]$NoBump
    )
    $local = Read-WslcAgentLocalVersions
    $display = Get-WslcAgentHigherVersion (Get-WslcAgentTrackedValue $Csproj "ApplicationDisplayVersion") $local["WslcLocalClientVersion"]
    $build = [Math]::Max([int](Get-WslcAgentTrackedValue $Csproj "ApplicationVersion"), [int]$(if ($local["WslcLocalClientBuild"]) { $local["WslcLocalClientBuild"] } else { 0 }))
    if ($NoBump) {
        return [pscustomobject]@{ Display = $display; Build = $build }
    }
    $nextDisplay = Get-NextPatchVersion $display
    $nextBuild = $build + 1
    $local["WslcLocalClientVersion"] = $nextDisplay
    $local["WslcLocalClientBuild"] = "$nextBuild"
    Write-WslcAgentLocalVersions $local
    Write-Host "Bumped client version $display ($build) -> $nextDisplay ($nextBuild) (private\version.props)" -ForegroundColor Cyan
    return [pscustomobject]@{ Display = $nextDisplay; Build = $nextBuild }
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
