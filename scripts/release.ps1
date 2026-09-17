# Builds dist\, zips NetworkMonitor-win-x64.zip, and creates a GitHub Release.
# Usage:
#   .\scripts\release.ps1 -Version 1.1.0
#   .\scripts\release.ps1 -Version 1.1.0 -Draft
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$Draft,
    [switch]$SkipPushTag
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must look like 1.2.3 (got '$Version')."
}

$tag = "v$Version"
$csproj = Join-Path $root "src\NetworkMonitor\NetworkMonitor.csproj"
$build = Join-Path $root "scripts\build.ps1"
$dist = Join-Path $root "dist"
$zip = Join-Path $root "NetworkMonitor-win-x64.zip"

Write-Host "Bumping csproj Version to $Version ..."
$xml = Get-Content -Raw $csproj
$xml = [regex]::Replace($xml, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
$xml = [regex]::Replace($xml, '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$Version</InformationalVersion>")
Set-Content -Path $csproj -Value $xml -NoNewline

Write-Host "Building ..."
& $build
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed." }

if (-not (Test-Path (Join-Path $dist "NetworkMonitor.exe"))) {
    throw "dist\NetworkMonitor.exe missing after build."
}

if (Test-Path $zip) { Remove-Item $zip -Force }
Write-Host "Zipping $zip ..."
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -Force

Write-Host "Committing version bump (if any) ..."
git add -- $csproj
git commit -m "Release $tag" 2>$null
git push origin HEAD

if (-not $SkipPushTag) {
    git tag -f $tag
    git push origin $tag --force
}

$draftArgs = @()
if ($Draft) { $draftArgs += '--draft' }

Write-Host "Creating GitHub release $tag ..."
gh release create $tag $zip `
    --title "Network Monitor $Version" `
    --generate-notes `
    @draftArgs

Write-Host ""
Write-Host "Done. Asset: NetworkMonitor-win-x64.zip on $tag"
Write-Host "Repo: https://github.com/cirobrandao/network-monitor/releases"
