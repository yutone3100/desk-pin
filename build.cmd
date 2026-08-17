@echo off
setlocal

set "CONFIGURATION=%~1"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Release"

dotnet test "%~dp0tests\DeskPin.Tests\DeskPin.Tests.csproj" -c "%CONFIGURATION%" --nologo
if errorlevel 1 exit /b %errorlevel%

dotnet build "%~dp0installer\DeskPin.Installer\DeskPin.Installer.wixproj" -c "%CONFIGURATION%" -t:Rebuild --nologo
if errorlevel 1 exit /b %errorlevel%

set "MSI_PATH=%~dp0installer\DeskPin.Installer\bin\x64\%CONFIGURATION%\zh-CN\DeskPin-x64.msi"
if not exist "%MSI_PATH%" set "MSI_PATH=%~dp0installer\DeskPin.Installer\bin\x64\%CONFIGURATION%\DeskPin-x64.msi"
set "EXE_PATH=%~dp0artifacts\publish\DeskPin.exe"
for %%I in ("%EXE_PATH%") do set "EXE_SIZE=%%~zI"
for %%I in ("%MSI_PATH%") do set "MSI_SIZE=%%~zI"
echo DeskPin EXE: %EXE_PATH% (%EXE_SIZE% bytes)
echo DeskPin MSI: %MSI_PATH%
echo DeskPin MSI size: %MSI_SIZE% bytes
if %EXE_SIZE% GTR 62914560 goto exe_too_large
if %MSI_SIZE% GTR 57671680 goto msi_too_large
exit /b 0

:exe_too_large
echo DeskPin EXE exceeds the 60 MiB size limit.
exit /b 1

:msi_too_large
echo DeskPin MSI exceeds the 55 MiB size limit.
exit /b 1
