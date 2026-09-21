# Gera artifacts\NetworkMonitor-Setup-<versao>.exe (instalador por usuario, sem admin).
[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = Join-Path $root "src\NetworkMonitor\NetworkMonitor.csproj"
$xml = [xml](Get-Content -Raw $csproj)
$version = $xml.Project.PropertyGroup.Version | Select-Object -First 1
if (-not $version) { throw "Nao achei <Version> no csproj." }

$dist = Join-Path $root "dist"
$setupIss = Join-Path $root "setup\network-monitor.iss"
$artifacts = Join-Path $root "artifacts"
$tools = Join-Path $root "tools\innosetup"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

if (-not $SkipBuild) {
    Write-Host "Compilando o app $version ..."
    & (Join-Path $PSScriptRoot "build.ps1")
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 falhou." }
}

$exe = Join-Path $dist "NetworkMonitor.exe"
if (-not (Test-Path $exe)) {
    throw "Falta $exe. Rode sem -SkipBuild."
}

function Find-Iscc {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidates = @(
        (Join-Path $tools "tools\ISCC.exe"),
        (Join-Path $tools "ISCC.exe"),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if (Test-Path $path) { return $path }
    }
    return $null
}

function Install-InnoSetupCompiler {
    Write-Host "Baixando o compilador Inno Setup (NuGet Tools.InnoSetup) ..."
    $nupkg = Join-Path $env:TEMP "Tools.InnoSetup.nupkg"
    $url = "https://api.nuget.org/v3-flatcontainer/tools.innosetup/6.4.3/tools.innosetup.6.4.3.nupkg"
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    if (Test-Path $tools) { Remove-Item $tools -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($nupkg, $tools)
    Remove-Item $nupkg -Force -ErrorAction SilentlyContinue
}

$iscc = Find-Iscc
if (-not $iscc) {
    Install-InnoSetupCompiler
    $iscc = Find-Iscc
}
if (-not $iscc) { throw "ISCC.exe nao encontrado." }

Write-Host "ISCC : $iscc"
Write-Host "Gerando instalador $version ..."

& $iscc `
    "/DAppVersion=$version" `
    "/DDistDir=$dist" `
    "/DOutputDir=$artifacts" `
    $setupIss

if ($LASTEXITCODE -ne 0) { throw "Inno Setup falhou (codigo $LASTEXITCODE)." }

$setup = Join-Path $artifacts "NetworkMonitor-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "Nao gerou $setup" }

Get-Item $setup | ForEach-Object {
    Write-Host ""
    Write-Host "INSTALADOR: $($_.FullName)"
    Write-Host ("TAMANHO   : {0:N1} MB" -f ($_.Length / 1MB))
    Write-Host "Pode enviar esse EXE aos amigos. Nao precisa de administrador."
}
