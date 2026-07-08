#requires -Version 5.1
param(
    [string]$Version = '2026.0.1',
    [string]$AppDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'publish\conclave-windows-x64'),
    [string]$OutDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'artifacts\native\windows')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stage = Join-Path $OutDir 'wix-staging'
$wxs = Join-Path $stage 'Conclave.wxs'
$msi = Join-Path $OutDir "Conclave-$Version-windows-x64.msi"

if (!(Test-Path (Join-Path $AppDir 'Conclave.Web.dll'))) {
    throw "AppDir must contain published Conclave.Web.dll: $AppDir"
}

New-Item -ItemType Directory -Force -Path $stage, $OutDir | Out-Null
$installerDir = Join-Path $stage 'installers\windows'
New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot '*') -Destination $installerDir -Recurse -Force

$wixAvailable = [bool](Get-Command wix.exe -ErrorAction SilentlyContinue) -or [bool](Get-Command candle.exe -ErrorAction SilentlyContinue)

$wxsTemplate = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="Conclave" Manufacturer="Conclave" Version="$Version" UpgradeCode="11111111-2222-4333-8444-555555555555">
    <SummaryInformation Description="Conclave Windows x64 MSI staging package" />
    <MajorUpgrade DowngradeErrorMessage="A newer version of Conclave is already installed." />
    <MediaTemplate />
    <Feature Id="Main" Title="Conclave" Level="1">
      <ComponentGroupRef Id="ConclaveFiles" />
    </Feature>
  </Package>
</Wix>
"@
$wxsTemplate | Set-Content -Path $wxs -Encoding UTF8

if ($wixAvailable -and (Get-Command wix.exe -ErrorAction SilentlyContinue)) {
    wix.exe build $wxs -out $msi
} elseif ($wixAvailable) {
    $wixobj = Join-Path $stage 'Conclave.wixobj'
    candle.exe -out $wixobj $wxs
    light.exe -out $msi $wixobj
} else {
    $placeholder = Join-Path $OutDir "Conclave-$Version-windows-x64-msi-staging.txt"
    @"
Conclave-$Version-windows-x64.msi was not built on this workstation because WiX is not installed.

This script stages the WiX/MSI path for Phase C.3 and should be run on a Windows build host with WiX Toolset installed.
For tomorrow's Windows x64 VMware test, use the ZIP package and run:
  Set-ExecutionPolicy -Scope Process Bypass
  .\installers\windows\Install-ConclaveGui.ps1

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
