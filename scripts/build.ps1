# Por omissao publica em pasta (sem single-file), que reduz falsos positivos de SmartScreen/antivirus.
[CmdletBinding()]
param(
    [switch]$SingleFile,
    [string]$CertThumbprint,
    [string]$PfxPath,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$project = Join-Path $root "src\NetworkMonitor\NetworkMonitor.csproj"
$out = Join-Path $root "dist"
$single = if ($SingleFile) { "true" } else { "false" }

if (Test-Path $out) {
    Get-ChildItem $out -Force | Remove-Item -Recurse -Force
}

& $dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=$single `
    -p:IncludeNativeLibrariesForSelfExtract=$single `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $out

if ($LASTEXITCODE -ne 0) {
    throw "Falha ao publicar Network Monitor (codigo $LASTEXITCODE)."
}

function Resolve-SignTool {
    $cmd = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $kit = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kit)) { return $null }
    Get-ChildItem $kit -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\x64\\" } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$exe = Join-Path $out "NetworkMonitor.exe"
if ($CertThumbprint -or $PfxPath) {
    $signtool = Resolve-SignTool
    if (-not $signtool) {
        Write-Warning "signtool.exe nao encontrado (instale o Windows SDK). A assinatura foi ignorada."
    }
    else {
        $signArgs = @("sign", "/fd", "SHA256", "/tr", $TimestampUrl, "/td", "SHA256")
        if ($CertThumbprint) { $signArgs += @("/sha1", $CertThumbprint) }
        else { $signArgs += @("/f", $PfxPath) }
        $signArgs += $exe
        & $signtool @signArgs
        if ($LASTEXITCODE -ne 0) { throw "Falha ao assinar $exe" }
    }
}

Get-ChildItem $out -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "EXE     : $exe"
Write-Host "Formato : $(if ($SingleFile) { 'ficheiro unico' } else { 'pasta (recomendado)' })"
Write-Host "Assinado: $(if ($CertThumbprint -or $PfxPath) { 'sim' } else { 'nao - o SmartScreen pode avisar no ficheiro unico' })"
