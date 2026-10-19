#requires -Version 5.1
param(
    [string]$Version = '1.0.0',
    [string]$AppDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'publish\arkanum-windows-x64'),
    [string]$OutDir = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path 'artifacts\native\windows')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Arkanum-Version.ps1')
Assert-ArkanumPackageVersion $Version
if (!(Test-Path (Join-Path $AppDir 'Arkanum.Web.dll'))) {
    throw "AppDir must contain published Arkanum.Web.dll: $AppDir"
}

# The previous WiX template referenced an undefined ArkanumFiles component
# group and was never a supported MSI. Do not pretend MajorUpgrade handles the
# calendar -> SemVer reset, or produce an unverified lower ProductVersion MSI.
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$placeholder = Join-Path $OutDir "Arkanum-$Version-windows-x64-msi-staging.txt"
@"
MSI remains unsupported for installation/upgrade, including 2026.1.2 -> $Version.
No .msi was built. WiX authoring, a monotonic native version mapping, and native
upgrade/repair/downgrade preservation acceptance are outstanding.
Use Arkanum-$Version-full-windows-x64.zip or Arkanum-$Version-update-windows-x64.zip
and the supported Windows PowerShell 5.1 installer entry points:
  .\installers\windows\Install-ArkanumGui.ps1
  .\installers\windows\Install-Arkanum.ps1
Native Windows IIS acceptance remains issue #28; staging is not installation proof.
"@ | Set-Content -Path $placeholder -Encoding UTF8
Write-Warning 'MSI remains unsupported for installation/upgrade. Wrote staging instructions only; use ZIP.'
Write-Host "MSI staging instructions: $placeholder"
