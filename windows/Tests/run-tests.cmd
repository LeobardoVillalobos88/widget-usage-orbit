@echo off
setlocal
cd /d "%~dp0.."
if not exist bin mkdir bin
set "FRAMEWORK=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
"%FRAMEWORK%\csc.exe" /nologo /langversion:5 /warn:4 /warnaserror+ /target:exe /out:bin\UsageOrbit.Tests.exe /reference:"%FRAMEWORK%\System.Web.Extensions.dll" /reference:"%FRAMEWORK%\System.Net.Http.dll" Tests\ContractTests.cs Data\*.cs System\ChildProcess.cs System\JobObject.cs
if errorlevel 1 exit /b 1
bin\UsageOrbit.Tests.exe
exit /b %errorlevel%
