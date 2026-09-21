# Remove instalacao por usuario do Network Monitor.
[CmdletBinding()]
param()
$ErrorActionPreference = "Stop"
$target = Join-Path $env:LOCALAPPDATA "NetworkMonitor"
$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Network Monitor.lnk"
$reg = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitor"
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Remove-ItemProperty -Path $run -Name "NetworkMonitor" -ErrorAction SilentlyContinue
Remove-Item -Path $reg -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $shortcutPath -Force -ErrorAction SilentlyContinue

# Nao apaga a pasta se o script esta rodando de dentro dela — agenda remocao
$self = $MyInvocation.MyCommand.Path
if ($self -like "$target*") {
    $cmd = "Start-Sleep -Seconds 2; Remove-Item -LiteralPath '$target' -Recurse -Force -ErrorAction SilentlyContinue"
    Start-Process powershell -ArgumentList "-NoProfile -Command $cmd" -WindowStyle Hidden
    Write-Host "UNINSTALL_SCHEDULED: $target"
} else {
    if (Test-Path $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    Write-Host "UNINSTALLED: $target"
}