#requires -Version 5.1
param(
    [string]$Version = '2026.1.1',
    [string]$AppDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'publish\arkanum-windows-x64'),
    [string]$OutDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'artifacts\native\windows')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stage = Join-Path $OutDir 'wix-staging'
$wxs = Join-Path $stage 'Arkanum.wxs'
$msi = Join-Path $OutDir "Arkanum-$Version-windows-x64.msi"

if (!(Test-Path (Join-Path $AppDir 'Arkanum.Web.dll'))) {
    throw "AppDir must contain published Arkanum.Web.dll: $AppDir"
}

New-Item -ItemType Directory -Force -Path $stage, $OutDir | Out-Null
$installerDir = Join-Path $stage 'installers\windows'
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '*') -Destination $installerDir -Recurse -Force

$wixAvailable = [bool](Get-Command wix.exe -ErrorAction SilentlyContinue) -or [bool](Get-Command candle.exe -ErrorAction SilentlyContinue)

$wxsTemplate = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Arkanum" Manufacturer="Arkanum" Version="$Version" UpgradeCode="11111111-2222-4333-8444-555555555555">
    <SummaryInformation Description="Arkanum Windows x64 MSI staging package" />
    <MajorUpgrade DowngradeErrorMessage="A newer version of Arkanum is already installed." />
    <MediaTemplate />
    <Feature Id="Main" Title="Arkanum" Level="1">
      <ComponentGroupRef Id="ArkanumFiles" />
    </Feature>
  </Package>
</Wix>
"@
$wxsTemplate | Set-Content -Path $wxs -Encoding UTF8

if ($wixAvailable -and (Get-Command wix.exe -ErrorAction SilentlyContinue)) {
    wix.exe build $wxs -out $msi
} elseif ($wixAvailable) {
    $wixobj = Join-Path $stage 'Arkanum.wixobj'
    candle.exe -out $wixobj $wxs
    light.exe -out $msi $wixobj
} else {
    $placeholder = Join-Path $OutDir "Arkanum-$Version-windows-x64-msi-staging.txt"
    @"
Arkanum-$Version-windows-x64.msi was not built on this workstation because WiX is not installed.

This script stages the WiX/MSI path for Phase C.3 and should be run on a Windows build host with WiX Toolset installed.
For tomorrow's Windows x64 VMware test, use the ZIP package and run:
  Set-ExecutionPolicy -Scope Process Bypass
  .\installers\windows\Install-ArkanumGui.ps1

Expected MSI output when WiX is available:
  $msi
"@ | Set-Content -Path $placeholder -Encoding UTF8
    Write-Warning 'WiX was not found. Wrote MSI staging instructions instead of a native MSI.'
}

if (Test-Path $msi) {
    Get-FileHash -Algorithm SHA256 $msi | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $msi -Leaf)" } | Set-Content -Path "$msi.sha256" -Encoding ASCII
    Write-Host "Built $msi"
} else {
    Write-Host "WiX MSI staging complete under $OutDir"
}
