# Asked once the Windows client is uninstalled: whether to remove its
# preferences too. They live in the WebView2 folder the client creates beside
# itself (the agents it knows, the theme, the views, the dashboard kept on this
# PC), which the installer never installed. Yes removes it, and the install
# folder if nothing else is left; No keeps them for a later install.
#
# build-client-installer.ps1 encodes this file into the installer, which runs
# it with no window of its own (Package.wxs): only the question is seen.
$folder = Join-Path $env:LOCALAPPDATA "WSLC-AI-Client"
$preferences = Join-Path $folder "wslc-ai-client.exe.WebView2"
if (-not (Test-Path -LiteralPath $preferences)) {
    return
}

# 4 Yes/No, 32 a question mark, 4096 on top of every window; 6 is Yes.
$question = "Remove your WSLC AI Client preferences too? The agents it knows, the theme, the views and the dashboard kept on this PC. No keeps them for a later install."
$answer = (New-Object -ComObject WScript.Shell).Popup($question, 0, "Uninstall WSLC AI Client", 4 + 32 + 4096)
if ($answer -eq 6) {
    Remove-Item -LiteralPath $preferences -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $folder -Force -ErrorAction SilentlyContinue
    }
}
