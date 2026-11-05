#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$DeviceId,
      [Parameter(Mandatory=$true)][uri]$Endpoint,
      [string]$SourceRoot=$PSScriptRoot)
$ErrorActionPreference='Stop'
function Invoke-Native([string]$Command,[string[]]$Arguments) {
    # Windows PowerShell 5.1 turns native stderr into ErrorRecord objects. Observe
    # a fresh exit code instead of allowing EAP=Stop to abort before that check.
    $previous=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        $global:LASTEXITCODE=$null
        $output=& $Command @Arguments 2>&1
        $exit=$global:LASTEXITCODE
    } finally { $ErrorActionPreference=$previous }
    foreach($line in $output) { Write-Output ([string]$line) }
    if ($null -eq $exit -or $exit -ne 0) { throw 'Native proof command failed' }
}
if ([string]::IsNullOrWhiteSpace($DeviceId) -or $Endpoint.Scheme -cne 'https' -or
    $Endpoint.AbsolutePath -cne '/sprint1/sync' -or $Endpoint.UserInfo -or $Endpoint.Query -or $Endpoint.Fragment) { throw 'Invalid proof configuration' }
if (Get-Service ArkanumGoSyncProof -ErrorAction SilentlyContinue) { throw 'Service exists; uninstall explicitly before replacement' }
$Target=Join-Path $env:ProgramFiles 'ArkanumGoSyncProof'
$Data=Join-Path $env:ProgramData 'ArkanumGoSyncProof'
if (!(Test-Path (Join-Path $SourceRoot 'Arkanum.Go.Proof.Windows.exe'))) { throw 'Use the published win-x64 directory' }
if(Test-Path -LiteralPath $Target) { throw 'Install destination already exists; remove old proof installation explicitly' }
. (Join-Path $SourceRoot 'acl.ps1')
if(Test-Path -LiteralPath $Data) { Assert-ProofTrustedTree $Data }
$Config=Join-Path $Data 'config.json'
if (Test-Path -LiteralPath $Config) {
    $existing=[IO.File]::ReadAllText($Config) | ConvertFrom-Json
    if ($existing.DeviceId -cne $DeviceId -or $existing.Endpoint -cne $Endpoint.AbsoluteUri) {
        throw 'Preserved proof configuration differs; explicit re-enrollment required'
    }
}
New-Item -ItemType Directory $Target | Out-Null
if(!(Test-Path -LiteralPath $Data)) { New-Item -ItemType Directory $Data | Out-Null }
Assert-ProofTrustedTree $Data
Copy-Item (Join-Path $SourceRoot '*') $Target -Force -Recurse
$Binary='"'+(Join-Path $Target 'Arkanum.Go.Proof.Windows.exe')+'" --service'
# New-Service preserves embedded executable-path quotes on Windows PowerShell 5.1.
# Configure the virtual identity while stopped, before any possible first launch.
New-Service -Name ArkanumGoSyncProof -BinaryPathName $Binary -StartupType Automatic -DisplayName 'Arkanum Go Sprint1 Proof' | Out-Null
Invoke-Native 'sc.exe' @('config','ArkanumGoSyncProof','obj=','NT SERVICE\ArkanumGoSyncProof')
Invoke-Native 'sc.exe' @('sidtype','ArkanumGoSyncProof','unrestricted')
Invoke-Native 'sc.exe' @('sdset','ArkanumGoSyncProof','D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;LC;;;AU)')
. (Join-Path $SourceRoot 'acl.ps1')
Set-ProofAcl $Target $true
Set-ProofAcl $Data $false
$json=@{DeviceId=$DeviceId;Endpoint=$Endpoint.AbsoluteUri} | ConvertTo-Json
if (!(Test-Path -LiteralPath $Config)) {
    [IO.File]::WriteAllText((Join-Path $Data 'config.json'),$json,(New-Object Text.UTF8Encoding($false)))
} # Keep exact bytes on reinstall: an unconsumed bootstrap receipt binds this hash.
Invoke-Native 'sc.exe' @('failure','ArkanumGoSyncProof','reset=','86400','actions=','restart/5000/restart/15000/restart/60000')
Invoke-Native (Join-Path $Target 'Arkanum.Go.Proof.Windows.exe') @('--provision')
Start-Service ArkanumGoSyncProof
(Get-Service ArkanumGoSyncProof).WaitForStatus('Running',[TimeSpan]::FromSeconds(20))
Write-Output 'Installed. Initialization creates auth.csr.pem and unwrap.spki under protected ProgramData. Enrollment is still required.'
