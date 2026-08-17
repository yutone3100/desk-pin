@echo off
setlocal

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

dotnet test "%~dp0tests\DeskPin.Tests\DeskPin.Tests.csproj" -c "%CONFIGURATION%" --nologo
if errorlevel 1 exit /b %errorlevel%

dotnet build "%~dp0installer\DeskPin.Installer\DeskPin.Installer.wixproj" -c "%CONFIGURATION%" --nologo
if errorlevel 1 exit /b %errorlevel%

echo DeskPin MSI: %~dp0installer\DeskPin.Installer\bin\x64\%CONFIGURATION%\DeskPin-x64.msi
