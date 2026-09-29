@echo off
setlocal
title Instalador de CastDecoy
cd /d "%~dp0"
echo ========================================================
echo               Instalador de CastDecoy
echo ========================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
echo.
echo Presione cualquier tecla para cerrar esta ventana...
pause >nul
