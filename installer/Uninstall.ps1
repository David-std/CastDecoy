# CastDecoy - Automated Uninstaller
[CmdletBinding()]
param ()

$ErrorActionPreference = "SilentlyContinue"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "     Desinstalador de CastDecoy          " -ForegroundColor White
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/4] Cerrando CastDecoy..." -ForegroundColor Yellow
Get-Process CastDecoy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "[2/4] Eliminando accesos directos..." -ForegroundColor Yellow
$desktopLnk = "$env:USERPROFILE\Desktop\CastDecoy.lnk"
if (Test-Path $desktopLnk) { Remove-Item $desktopLnk -Force }

$startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\CastDecoy"
if (Test-Path $startMenuDir) { Remove-Item $startMenuDir -Recurse -Force }

Write-Host "[3/4] Eliminando entradas del registro de Windows..." -ForegroundColor Yellow
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CastDecoy"
if (Test-Path $regPath) { Remove-Item $regPath -Recurse -Force }

Write-Host "[4/4] Eliminando archivos de aplicacion..." -ForegroundColor Yellow
$installDir = "$env:LOCALAPPDATA\Programs\CastDecoy"
if (Test-Path $installDir) {
    # Eliminar todo excepto este script si se esta ejecutando desde aqui
    Get-ChildItem $installDir -Exclude "Uninstall.cmd", "Uninstall.ps1" | Remove-Item -Recurse -Force
}

Write-Host ""
Write-Host "CastDecoy ha sido desinstalado correctamente de tu equipo." -ForegroundColor Green
Write-Host ""
