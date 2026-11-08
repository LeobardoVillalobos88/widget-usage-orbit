@echo off
setlocal
cd /d "%~dp0"
set "FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist bin mkdir bin
if not exist assets\app.ico (
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File assets\make-icon.ps1
  if errorlevel 1 exit /b 1
)
"%FRAMEWORK%\csc.exe" /nologo /langversion:5 /codepage:65001 /warn:4 /warnaserror+ /optimize+ /target:winexe /platform:anycpu /out:bin\UsageOrbit.exe /win32manifest:app.manifest /win32icon:assets\app.ico /reference:"%FRAMEWORK%\WPF\PresentationFramework.dll" /reference:"%FRAMEWORK%\WPF\PresentationCore.dll" /reference:"%FRAMEWORK%\WPF\WindowsBase.dll" /reference:"%FRAMEWORK%\System.Xaml.dll" /reference:"%FRAMEWORK%\System.Web.Extensions.dll" /reference:"%FRAMEWORK%\System.Net.Http.dll" Program.cs App\*.cs UI\*.cs Data\*.cs System\*.cs
if errorlevel 1 exit /b 1
copy /y app.config bin\UsageOrbit.exe.config >nul
if errorlevel 1 exit /b 1
echo Compilacion correcta: 0 errores, 0 advertencias.
exit /b 0
