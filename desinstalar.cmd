@echo off
rem Desinstala Usage Orbit: quita solo sus archivos, accesos directos e inicio automatico.
if exist "%LOCALAPPDATA%\UsageOrbit\UsageOrbit.exe" (
  start "" "%LOCALAPPDATA%\UsageOrbit\UsageOrbit.exe" --desinstalar
) else (
  echo Usage Orbit no esta instalado.
  pause
)
