<#
.SYNOPSIS
    One-click installer for the "Chill More Todo Text" BepInEx mod.

.DESCRIPTION
    Walks the full setup: locate the game (auto-detect via Steam, or browse),
    check whether BepInEx 5 is installed, download + extract it if missing, then
    copy the plugin into BepInEx\plugins. Runs as a small GUI by default; pass
    -NoGui (and optionally -GamePath) to run headless.

.PARAMETER GamePath
    Path to the game root folder (the one containing "Chill With You.exe").
    If omitted, the installer auto-detects via Steam or asks.

.PARAMETER NoGui
    Run on the console without the GUI window.
#>
[CmdletBinding()]
param(
    [string]$GamePath,
    [switch]$NoGui
)

$ErrorActionPreference = 'Stop'

# ----- constants -------------------------------------------------------------
$BepInExVersion = '5.4.23.2'
$BepInExUrl     = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
$GameExe        = 'Chill With You.exe'
$GameFolderName = 'Chill with You Lo-Fi Story'
$PluginName     = 'ChillMoreTodoText.dll'
$ScriptDir      = Split-Path -Parent $MyInvocation.MyCommand.Path
# Plugin DLL ships in ..\dist next to this installer; fall back to a sibling copy.
$PluginSource   = @(
    (Join-Path $ScriptDir "..\dist\$PluginName"),
    (Join-Path $ScriptDir $PluginName)
) | Where-Object { Test-Path $_ } | Select-Object -First 1

# ----- logging hook (overridden by GUI) --------------------------------------
$script:LogSink = { param($msg) Write-Host $msg }
function Log([string]$msg) { & $script:LogSink $msg }

# ----- core logic ------------------------------------------------------------
function Find-GameViaSteam {
    # Returns the game root path if found through Steam library folders, else $null.
    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -Name SteamPath -ErrorAction Stop).SteamPath
    } catch { $steam = $null }

    $libs = New-Object System.Collections.Generic.List[string]
    if ($steam) { $libs.Add($steam) }

    $vdf = if ($steam) { Join-Path $steam 'steamapps\libraryfolders.vdf' } else { $null }
    if ($vdf -and (Test-Path $vdf)) {
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
            $libs.Add($m.Groups[1].Value.Replace('\\', '\'))
        }
    }
    # Also probe every fixed drive's common Steam layout, just in case.
    foreach ($d in (Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Free -ne $null })) {
        $libs.Add("$($d.Root)SteamLibrary")
        $libs.Add("$($d.Root)Program Files (x86)\Steam")
    }

    foreach ($lib in ($libs | Select-Object -Unique)) {
        $candidate = Join-Path $lib "steamapps\common\$GameFolderName"
        if (Test-Path (Join-Path $candidate $GameExe)) { return $candidate }
    }
    return $null
}

function Test-GamePath([string]$path) {
    return ($path -and (Test-Path (Join-Path $path $GameExe)))
}

function Test-BepInExInstalled([string]$gameRoot) {
    return (Test-Path (Join-Path $gameRoot 'winhttp.dll')) -and
           (Test-Path (Join-Path $gameRoot 'BepInEx\core\BepInEx.dll'))
}

function Install-BepInEx([string]$gameRoot) {
    Log "Downloading BepInEx $BepInExVersion ..."
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "BepInEx_$BepInExVersion.zip"
    $old = $ProgressPreference; $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $BepInExUrl -OutFile $tmp
    } finally { $ProgressPreference = $old }
    Log ("Downloaded {0:N1} MB. Extracting into game folder ..." -f ((Get-Item $tmp).Length / 1MB))
    Expand-Archive -Path $tmp -DestinationPath $gameRoot -Force
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    if (-not (Test-BepInExInstalled $gameRoot)) {
        throw "BepInEx files not found after extraction. Install aborted."
    }
    Log "BepInEx installed."
}

function Install-Plugin([string]$gameRoot) {
    if (-not $PluginSource) {
        throw "Could not find $PluginName (expected in ..\dist next to the installer)."
    }
    $pluginsDir = Join-Path $gameRoot 'BepInEx\plugins'
    if (-not (Test-Path $pluginsDir)) { New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null }
    Copy-Item $PluginSource (Join-Path $pluginsDir $PluginName) -Force
    Log "Plugin copied to BepInEx\plugins\$PluginName"
}

function Invoke-Install([string]$gameRoot) {
    if (-not (Test-GamePath $gameRoot)) {
        throw "'$GameExe' not found in:`n$gameRoot"
    }
    Log "Game folder: $gameRoot"

    if (Test-BepInExInstalled $gameRoot) {
        Log "BepInEx already installed - skipping download."
    } else {
        Log "BepInEx not found."
        Install-BepInEx $gameRoot
    }

    Install-Plugin $gameRoot
    Log ""
    Log "DONE. Launch the game normally."
    Log "  - To-do text boxes now hold much more text (no more '...' cut-off)."
    Log "Config (after first run): BepInEx\config\com.flamfoof.chillmoretodotext.cfg"
}

# ----- headless mode ---------------------------------------------------------
if ($NoGui) {
    if (-not $GamePath) { $GamePath = Find-GameViaSteam }
    if (-not $GamePath) { throw "Could not auto-detect the game. Re-run with -GamePath '<path to game folder>'." }
    Invoke-Install $GamePath
    return
}

# ----- GUI mode --------------------------------------------------------------
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object Windows.Forms.Form
$form.Text = "Chill More Todo Text - Installer"
$form.Size = New-Object Drawing.Size(640, 460)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedSingle'
$form.MaximizeBox = $false

$lbl = New-Object Windows.Forms.Label
$lbl.Text = "Game folder (contains '$GameExe'):"
$lbl.Location = New-Object Drawing.Point(12, 15)
$lbl.AutoSize = $true
$form.Controls.Add($lbl)

$txtPath = New-Object Windows.Forms.TextBox
$txtPath.Location = New-Object Drawing.Point(12, 38)
$txtPath.Size = New-Object Drawing.Size(480, 23)
$form.Controls.Add($txtPath)

$btnBrowse = New-Object Windows.Forms.Button
$btnBrowse.Text = "Browse..."
$btnBrowse.Location = New-Object Drawing.Point(500, 37)
$btnBrowse.Size = New-Object Drawing.Size(110, 25)
$form.Controls.Add($btnBrowse)

$btnDetect = New-Object Windows.Forms.Button
$btnDetect.Text = "Auto-detect via Steam"
$btnDetect.Location = New-Object Drawing.Point(12, 70)
$btnDetect.Size = New-Object Drawing.Size(180, 27)
$form.Controls.Add($btnDetect)

$btnInstall = New-Object Windows.Forms.Button
$btnInstall.Text = "Install / Update Mod"
$btnInstall.Location = New-Object Drawing.Point(430, 70)
$btnInstall.Size = New-Object Drawing.Size(180, 27)
$btnInstall.BackColor = [Drawing.Color]::FromArgb(76, 175, 80)
$btnInstall.ForeColor = [Drawing.Color]::White
$form.Controls.Add($btnInstall)

$txtLog = New-Object Windows.Forms.TextBox
$txtLog.Location = New-Object Drawing.Point(12, 110)
$txtLog.Size = New-Object Drawing.Size(598, 300)
$txtLog.Multiline = $true
$txtLog.ScrollBars = 'Vertical'
$txtLog.ReadOnly = $true
$txtLog.BackColor = [Drawing.Color]::FromArgb(30, 30, 30)
$txtLog.ForeColor = [Drawing.Color]::Gainsboro
$txtLog.Font = New-Object Drawing.Font("Consolas", 9)
$form.Controls.Add($txtLog)

# Route Log() into the textbox.
$script:LogSink = {
    param($msg)
    $txtLog.AppendText("$msg`r`n")
    $txtLog.SelectionStart = $txtLog.Text.Length
    $txtLog.ScrollToCaret()
    [Windows.Forms.Application]::DoEvents()
}

$btnBrowse.Add_Click({
    $dlg = New-Object Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select the game folder (contains '$GameExe')"
    if ($dlg.ShowDialog() -eq 'OK') { $txtPath.Text = $dlg.SelectedPath }
})

$btnDetect.Add_Click({
    Log "Searching Steam libraries..."
    $found = Find-GameViaSteam
    if ($found) { $txtPath.Text = $found; Log "Found: $found" }
    else { Log "Not found automatically. Use Browse to pick the folder." }
})

$btnInstall.Add_Click({
    $btnInstall.Enabled = $false
    try { Invoke-Install $txtPath.Text.Trim() }
    catch {
        Log "ERROR: $($_.Exception.Message)"
        [Windows.Forms.MessageBox]::Show($_.Exception.Message, "Install failed",
            'OK', 'Error') | Out-Null
    }
    finally { $btnInstall.Enabled = $true }
})

# Pre-fill: explicit param, else auto-detect.
if ($GamePath -and (Test-GamePath $GamePath)) { $txtPath.Text = $GamePath }
else {
    $auto = Find-GameViaSteam
    if ($auto) { $txtPath.Text = $auto; Log "Auto-detected game: $auto" }
    else { Log "Could not auto-detect the game - use Auto-detect or Browse." }
}
if (-not $PluginSource) { Log "WARNING: $PluginName not found near the installer (dist\ missing)." }

[void]$form.ShowDialog()
