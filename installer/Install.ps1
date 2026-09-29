# CastDecoy - Automated Windows Installer
[CmdletBinding()]
param (
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "       Instalador de CastDecoy           " -ForegroundColor White
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

$installDir = "$env:LOCALAPPDATA\Programs\CastDecoy"
$sourceDir = "$PSScriptRoot\..\bin\publish\win-x64"

if (-not (Test-Path "$sourceDir\CastDecoy.exe")) {
    $sourceDir = "$PSScriptRoot\..\bin\Release\net10.0-windows"
}

if (-not (Test-Path "$sourceDir\CastDecoy.exe")) {
    Write-Host "[ERROR] No se encontraron los archivos binarios compilados de CastDecoy en $sourceDir" -ForegroundColor Red
    Write-Host "Por favor compila la solucion primero ejecutando: dotnet build -c Release" -ForegroundColor Yellow
    exit 1
}

Write-Host "[1/5] Cerrando instancias activas de CastDecoy..." -ForegroundColor Yellow
Get-Process CastDecoy -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "[2/5] Copiando archivos del programa en $installDir..." -ForegroundColor Yellow
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Force -Path $installDir | Out-Null
}

Copy-Item "$sourceDir\*" -Destination $installDir -Recurse -Force

# Copiar script de desinstalacion en la carpeta de instalacion
Copy-Item "$PSScriptRoot\Uninstall.ps1" -Destination "$installDir\Uninstall.ps1" -Force -ErrorAction SilentlyContinue
Copy-Item "$PSScriptRoot\Uninstall.cmd" -Destination "$installDir\Uninstall.cmd" -Force -ErrorAction SilentlyContinue

Write-Host "[3/5] Creando accesos directos en Menu Inicio y Escritorio..." -ForegroundColor Yellow
$wsh = New-Object -ComObject WScript.Shell

# Acceso directo en Menu Inicio
$startMenuDir = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\CastDecoy"
if (-not (Test-Path $startMenuDir)) {
    New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
}
$startMenuShortcut = $wsh.CreateShortcut("$startMenuDir\CastDecoy.lnk")
$startMenuShortcut.TargetPath = "$installDir\CastDecoy.exe"
$startMenuShortcut.WorkingDirectory = $installDir
$startMenuShortcut.Description = "CastDecoy - Suite de Aislamiento de Pantalla y Senuelos"
$startMenuShortcut.IconLocation = "$installDir\CastDecoy.exe,0"
$startMenuShortcut.Save()

# Acceso directo de desinstalacion en Menu Inicio
$uninstallShortcut = $wsh.CreateShortcut("$startMenuDir\Desinstalar CastDecoy.lnk")
$uninstallShortcut.TargetPath = "$installDir\Uninstall.cmd"
$uninstallShortcut.WorkingDirectory = $installDir
$uninstallShortcut.Description = "Desinstalar CastDecoy"
$uninstallShortcut.IconLocation = "shell32.dll,31"
$uninstallShortcut.Save()

# Acceso directo en Escritorio
$desktopShortcut = $wsh.CreateShortcut("$env:USERPROFILE\Desktop\CastDecoy.lnk")
$desktopShortcut.TargetPath = "$installDir\CastDecoy.exe"
$desktopShortcut.WorkingDirectory = $installDir
$desktopShortcut.Description = "CastDecoy - Suite de Aislamiento de Pantalla y Senuelos"
$desktopShortcut.IconLocation = "$installDir\CastDecoy.exe,0"
$desktopShortcut.Save()

Write-Host "[4/5] Registrando en Aplicaciones instaladas de Windows..." -ForegroundColor Yellow
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CastDecoy"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}
Set-ItemProperty -Path $regPath -Name "DisplayName" -Value "CastDecoy"
Set-ItemProperty -Path $regPath -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $regPath -Name "Publisher" -Value "CastDecoy"
Set-ItemProperty -Path $regPath -Name "DisplayIcon" -Value "$installDir\CastDecoy.exe"
Set-ItemProperty -Path $regPath -Name "InstallLocation" -Value $installDir
Set-ItemProperty -Path $regPath -Name "UninstallString" -Value "`"$installDir\Uninstall.cmd`""
Set-ItemProperty -Path $regPath -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $regPath -Name "NoRepair" -Value 1 -Type DWord
Set-ItemProperty -Path $regPath -Name "EstimatedSize" -Value 78000 -Type DWord

Write-Host "[5/5] Instalacion completada exitosamente." -ForegroundColor Green
Write-Host ""
Write-Host "Ubicacion: $installDir" -ForegroundColor Gray
Write-Host "Accesos directos creados en el Escritorio y Menu Inicio." -ForegroundColor Gray
Write-Host ""

if (-not $NoLaunch) {
    Write-Host "Iniciando CastDecoy..." -ForegroundColor Cyan
    Start-Process "$installDir\CastDecoy.exe"
}
