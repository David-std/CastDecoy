@echo off
setlocal
title Desinstalador de CastDecoy
cd /d "%~dp0"
echo ========================================================
echo              Desinstalador de CastDecoy
echo ========================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1"
echo.
echo Presione cualquier tecla para salir...
pause >nul
