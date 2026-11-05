#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param([Parameter(Mandatory=$true)][string]$ProofRootCertificate)
$ErrorActionPreference='Stop'
# Explicit owner-selected public proof CA only. No name searches or broad trust-store sweeps.
$expected=New-Object Security.Cryptography.X509Certificates.X509Certificate2((Resolve-Path -LiteralPath $ProofRootCertificate).Path)
try {
    if($expected.Subject -cne $expected.Issuer) {throw 'Expected a self-signed proof root certificate'}
    $path='Cert:\LocalMachine\Root\'+$expected.Thumbprint
    $actual=Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if($actual) {
        if([Convert]::ToBase64String($actual.RawData) -cne [Convert]::ToBase64String($expected.RawData)) {throw 'Certificate mismatch'}
        if($PSCmdlet.ShouldProcess($expected.Thumbprint,'Remove this exact proof CA from LocalMachine Root')) {Remove-Item -LiteralPath $path}
        if((Test-Path -LiteralPath $path) -and !$WhatIfPreference) {throw 'Certificate removal not verified'}
    }
} finally {$expected.Dispose()}
