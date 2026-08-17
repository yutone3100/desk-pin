param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot

dotnet test "$projectRoot\tests\DeskPin.Tests\DeskPin.Tests.csproj" -c $Configuration --nologo
dotnet build "$projectRoot\installer\DeskPin.Installer\DeskPin.Installer.wixproj" -c $Configuration --nologo

$localizedMsi = "$projectRoot\installer\DeskPin.Installer\bin\x64\$Configuration\zh-CN\DeskPin-x64.msi"
$defaultMsi = "$projectRoot\installer\DeskPin.Installer\bin\x64\$Configuration\DeskPin-x64.msi"
$msiPath = if (Test-Path -LiteralPath $localizedMsi) { $localizedMsi } else { $defaultMsi }
$exePath = "$projectRoot\artifacts\publish\DeskPin.exe"
$exeSize = (Get-Item -LiteralPath $exePath).Length
$msiSize = (Get-Item -LiteralPath $msiPath).Length

Write-Host "DeskPin EXE: $exePath ($exeSize bytes, $([math]::Round($exeSize / 1MB, 2)) MiB)"
Write-Host "DeskPin MSI: $msiPath"
Write-Host "DeskPin MSI size: $msiSize bytes ($([math]::Round($msiSize / 1MB, 2)) MiB)"

if ($exeSize -gt 60MB) {
    throw "DeskPin EXE 超过 60 MiB 体积门槛"
}

if ($msiSize -gt 55MB) {
    throw "DeskPin MSI 超过 55 MiB 体积门槛"
}
