param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot

dotnet test "$projectRoot\tests\DeskPin.Tests\DeskPin.Tests.csproj" -c $Configuration --nologo
dotnet build "$projectRoot\installer\DeskPin.Installer\DeskPin.Installer.wixproj" -c $Configuration --nologo

Write-Host "DeskPin MSI: $projectRoot\installer\DeskPin.Installer\bin\x64\$Configuration\DeskPin-x64.msi"
