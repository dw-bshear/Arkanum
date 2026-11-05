#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess=$true,ConfirmImpact='High')]
param([switch]$PurgeDataAndKeys)
$ErrorActionPreference='Stop'
if(!$PSCmdlet.ShouldProcess('ArkanumGoSyncProof service and installation','Uninstall proof')) {return}
$service=Get-Service ArkanumGoSyncProof -ErrorAction SilentlyContinue
if($service) {
    Stop-Service ArkanumGoSyncProof -Force
    $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
    & sc.exe delete ArkanumGoSyncProof
    if($LASTEXITCODE -ne 0) {throw 'SCM delete failed'}
}
$Target=Join-Path $env:ProgramFiles 'ArkanumGoSyncProof'
if(Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Recurse -Force }
if($PurgeDataAndKeys -and $PSCmdlet.ShouldProcess('Proof data + ArkanumGoSyncProof CNG keys','Permanently erase (including sticky deny)')) {
    foreach($name in @('ArkanumGoSyncProof-auth','ArkanumGoSyncProof-unwrap')) {
        if([Security.Cryptography.CngKey]::Exists($name,[Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider,[Security.Cryptography.CngKeyOpenOptions]::MachineKey)) {
            $key=[Security.Cryptography.CngKey]::Open($name,[Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider,[Security.Cryptography.CngKeyOpenOptions]::MachineKey)
            try {$key.Delete()} finally {$key.Dispose()}
        }
    }
    $Data=Join-Path $env:ProgramData 'ArkanumGoSyncProof'
    if(Test-Path -LiteralPath $Data) { Remove-Item -LiteralPath $Data -Recurse -Force }
}
Write-Output 'Proof service removed. Data/keys preserved unless explicitly purged. Trust cleanup is separate: remove-proof-trust.ps1.'
