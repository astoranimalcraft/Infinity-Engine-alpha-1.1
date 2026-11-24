# Creates a Desktop shortcut to the Infinity Engine editor with the Infinity Engine icon.
# Run from anywhere:  powershell -ExecutionPolicy Bypass -File Tools\Create-Desktop-Shortcut.ps1

param([string]$ExePath = "")

$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $candidate = Get-ChildItem -Path (Join-Path $root "Build\Editor") -Filter *.exe -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $candidate) {
        throw "Build the editor first, then run this script again."
    }
    $ExePath = $candidate.FullName
}

$ExePath = (Resolve-Path $ExePath).Path
$icon = Join-Path $root "Prowl.Editor\Resources\Branding\InfinityEngine.ico"
$desktop = [Environment]::GetFolderPath("Desktop")
$link = Join-Path $desktop "Infinity Engine.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($link)
$shortcut.TargetPath = $ExePath
$shortcut.WorkingDirectory = Split-Path -Parent $ExePath
$shortcut.IconLocation = "$icon,0"
$shortcut.Description = "Infinity Engine"
$shortcut.Save()

# The PNG master is also placed on the Desktop, so the mark is available for docs and store pages.
$png = Join-Path $root "Prowl.Editor\Resources\Branding\InfinityEngine.png"
if (Test-Path $png) { Copy-Item $png (Join-Path $desktop "Infinity Engine.png") -Force }

Write-Host "Created $link"
