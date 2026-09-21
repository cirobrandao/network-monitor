# Instala Network Monitor para o usuario atual (sem admin).
# Uso: .\scripts\install-user.ps1 [-Startup] [-SourceDir caminho]
[CmdletBinding()]
param(
    [switch]$Startup,
    [string]$SourceDir
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $SourceDir) { $SourceDir = Join-Path $root "dist" }
if (-not (Test-Path (Join-Path $SourceDir "NetworkMonitor.exe"))) {
    throw "NetworkMonitor.exe nao encontrado em $SourceDir. Rode .\scripts\build.ps1 antes."
}

$target = Join-Path $env:LOCALAPPDATA "NetworkMonitor"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $SourceDir "*") -Destination $target -Recurse -Force

$exe = Join-Path $target "NetworkMonitor.exe"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
$shortcutPath = Join-Path $startMenu "Network Monitor.lnk"
$wscript = New-Object -ComObject WScript.Shell
$sc = $wscript.CreateShortcut($shortcutPath)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $target
$sc.Description = "Network Monitor"
$sc.Save()

$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$target\uninstall-user.ps1`""
Copy-Item -Force (Join-Path $PSScriptRoot "uninstall-user.ps1") (Join-Path $target "uninstall-user.ps1")

$reg = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor"
New-Item -Path $reg -Force | Out-Null
Set-ItemProperty -Path $reg -Name "DisplayName" -Value "Network Monitor"
Set-ItemProperty -Path $reg -Name "DisplayIcon" -Value $exe
Set-ItemProperty -Path $reg -Name "InstallLocation" -Value $target
Set-ItemProperty -Path $reg -Name "Publisher" -Value "Ciro Brandao"
Set-ItemProperty -Path $reg -Name "UninstallString" -Value $uninstallCmd
Set-ItemProperty -Path $reg -Name "DisplayVersion" -Value "1.1.0"
Set-ItemProperty -Path $reg -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $reg -Name "NoRepair" -Value 1 -Type DWord

if ($Startup) {
    $run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $run -Name "NetworkMonitor" -Value "`"$exe`""
}

Write-Host "INSTALLED: $exe"
Write-Host "Atalho: $shortcutPath"
if ($Startup) { Write-Host "Inicio com Windows: sim" }