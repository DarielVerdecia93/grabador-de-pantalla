# Genera el instalador MSI de Grabador de Pantalla.
# Requisitos: .NET SDK y WiX v6 (dotnet tool install --global wix --version 6.0.1
#             + wix extension add -g WixToolset.UI.wixext/6.0.1).
#
# Uso:  pwsh installer\build-msi.ps1 [-Version 1.0.0]
param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root "GravadorTela.WinUI"
$pub  = Join-Path $proj "bin\Release\net11.0-windows10.0.19041.0\win-x64\publish"
$dist = Join-Path $root "dist"

Write-Host "==> Publicando (Release, self-contained, win-x64)..."
dotnet publish $proj -c Release -r win-x64 --self-contained true

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$msi = Join-Path $dist "GrabadorDePantalla-$Version.msi"

Write-Host "==> Construyendo MSI..."
wix build (Join-Path $root "installer\Product.wxs") `
    -d "PublishDir=$pub" `
    -ext WixToolset.UI.wixext `
    -arch x64 `
    -o $msi

Write-Host "==> Listo: $msi"
Get-Item $msi | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
