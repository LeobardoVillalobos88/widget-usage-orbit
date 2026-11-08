@echo off
rem Instala o actualiza Usage Orbit para el usuario actual.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows\install.ps1" %*
pause
