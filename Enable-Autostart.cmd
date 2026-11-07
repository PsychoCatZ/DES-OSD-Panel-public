@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Autostart.ps1"
if errorlevel 1 (
    echo Autostart setup failed. See verification\autostart-error.txt if present.
) else (
    echo Elevated autostart enabled.
)
pause
