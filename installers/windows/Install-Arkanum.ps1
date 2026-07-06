param(
    [string]$PackagePath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$InstallPath = 'C:\Program Files\Arkanum\Web',
    [string]$ProgramDataPath = 'C:\ProgramData\Arkanum',
    [string]$SiteName = 'Arkanum',
    [int]$Port = 5068,
    [Parameter(Mandatory=$true)][string]$SqlConnectionString,
    [string]$AppPoolName = 'ArkanumAppPool',
    [string]$PackageVersion = '2026.0.3',
    # ValidateSet('install','upgrade','repair','side-by-side') plus auto detection for GUI defaults.
    [ValidateSet('install','upgrade','repair','side-by-side','auto')][string]$Action = 'auto'
)

$ErrorActionPreference = 'Stop'

function Write-ArkanumReport {
    param(
        [string]$ReportPath,
        [string]$HealthStatus,
        [string]$HealthBody,
        [string]$BackupPath
    )

    $lines = @(
        'Arkanum installer report',
        'Product: Arkanum',
        "Package version: $PackageVersion",
        "Install action: $Action",
        "Install path: $InstallPath",
        "ProgramData path: $ProgramDataPath",
        "Site name: $SiteName",
        "App pool name: $AppPoolName",
        "State file: $script:StatePath",
        "Generated UTC: $((Get-Date).ToUniversalTime().ToString('s'))Z",
        "Health URL: http://localhost:$Port/health",
        "Backup path: $BackupPath",
        "Post-install health status: $HealthStatus",
        $HealthBody
    )
    $lines | Set-Content -Path $ReportPath -Encoding UTF8
}

Import-Module WebAdministration

$appDll = Join-Path $PackagePath 'Arkanum.Web.dll'
if (!(Test-Path $appDll)) {
    throw "Published Arkanum.Web.dll was not found in PackagePath=$PackagePath. Run this from a published release package or pass -PackagePath."
}

$secretDir = Join-Path $ProgramDataPath 'secrets'
$configDir = Join-Path $ProgramDataPath 'config'
$logDir = Join-Path $ProgramDataPath 'logs'
$backupRoot = Join-Path $ProgramDataPath 'backups'
$script:StatePath = Join-Path $configDir 'install-state.json'
$reportPath = Join-Path $logDir "upgrade-report-$PackageVersion.txt"

$existingInstall = (Test-Path $script:StatePath) -or (Test-Path (Join-Path $InstallPath 'Arkanum.Web.dll'))
if ($Action -eq 'auto') {
    if ($existingInstall) { $Action = 'upgrade' } else { $Action = 'install' }
}

if ($Action -eq 'side-by-side') {
    $InstallPath = "$InstallPath-$PackageVersion"
    $ProgramDataPath = "$ProgramDataPath-$PackageVersion"
    $secretDir = Join-Path $ProgramDataPath 'secrets'
    $configDir = Join-Path $ProgramDataPath 'config'
    $logDir = Join-Path $ProgramDataPath 'logs'
    $backupRoot = Join-Path $ProgramDataPath 'backups'
    $script:StatePath = Join-Path $configDir 'install-state.json'
    $reportPath = Join-Path $logDir "upgrade-report-$PackageVersion.txt"
    $Action = 'install'
}

if ($Action -eq 'upgrade' -and -not $existingInstall) {
    throw 'ACTION=upgrade requested but no existing Arkanum installation was detected. Use -Action install or -Action auto.'
}

New-Item -ItemType Directory -Force -Path $InstallPath, $secretDir, $configDir, $logDir, $backupRoot | Out-Null

$backupPath = ''
if ($Action -in @('upgrade','repair')) {
    $installedVersion = 'unknown'
    if (Test-Path $script:StatePath) {
        try { $installedVersion = (Get-Content $script:StatePath -Raw | ConvertFrom-Json).installedVersion } catch { $installedVersion = 'unknown' }
    }
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
    $backupPath = Join-Path $backupRoot "$installedVersion-before-$PackageVersion-$stamp"
    New-Item -ItemType Directory -Force -Path $backupPath | Out-Null
    if (Test-Path $InstallPath) { Copy-Item -Path $InstallPath -Destination (Join-Path $backupPath 'app') -Recurse -Force }
    if (Test-Path $configDir) { Copy-Item -Path $configDir -Destination (Join-Path $backupPath 'config') -Recurse -Force }
}

if (Test-Path "IIS:\AppPools\$AppPoolName") {
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
}

Copy-Item -Path (Join-Path $PackagePath '*') -Destination $InstallPath -Recurse -Force

$keyPath = Join-Path $secretDir 'master-key.txt'
if (!(Test-Path $keyPath) -or [string]::IsNullOrWhiteSpace((Get-Content $keyPath -Raw))) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    'ark-recovery-v1.' + [Convert]::ToBase64String($bytes).TrimEnd('=') | Set-Content -Path $keyPath -NoNewline
}

if (!(Test-Path "IIS:\AppPools\$AppPoolName")) {
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -Value ApplicationPoolIdentity

$aclTargets = @($ProgramDataPath, $secretDir, $configDir, $logDir)
foreach ($target in $aclTargets) {
    $acl = Get-Acl $target
    $identity = "IIS AppPool\$AppPoolName"
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($identity, 'Read,Write,ListDirectory', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -Path $target -AclObject $acl
}

$configPath = Join-Path $configDir 'arkanum.runtime.json'
$runtimeJson = @{
    ASPNETCORE_ENVIRONMENT = 'Production'
    ConnectionStrings__DefaultConnection = $SqlConnectionString
    MasterKeyPath = $keyPath
} | ConvertTo-Json
Set-Content -Path $configPath -Value $runtimeJson

$masterKey = (Get-Content $keyPath -Raw).Trim()
$escapedSqlConnectionString = [System.Security.SecurityElement]::Escape($SqlConnectionString)
$escapedMasterKey = [System.Security.SecurityElement]::Escape($masterKey)

$webConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
    </handlers>
    <aspNetCore processPath="dotnet" arguments=".\Arkanum.Web.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
      <environmentVariables>
        <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        <environmentVariable name="ARKANUM_MASTER_KEY" value="$escapedMasterKey" />
        <environmentVariable name="ConnectionStrings__DefaultConnection" value="$escapedSqlConnectionString" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
"@
Set-Content -Path (Join-Path $InstallPath 'web.config') -Value $webConfig -Encoding UTF8

if (!(Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -Port $Port -PhysicalPath $InstallPath -ApplicationPool $AppPoolName | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $InstallPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}

$state = [ordered]@{
    product = 'Arkanum'
    installedVersion = $PackageVersion
    installedAtUtc = (Get-Date).ToUniversalTime().ToString('s') + 'Z'
    installAction = $Action
    installPath = $InstallPath
    programDataPath = $ProgramDataPath
    configPath = $configDir
    logPath = $logDir
    siteName = $SiteName
    appPoolName = $AppPoolName
    databaseProvider = 'SqlServer'
    packageChannel = 'prerelease'
    backupPath = $backupPath
}
$state | ConvertTo-Json | Set-Content -Path $script:StatePath -Encoding UTF8

Restart-WebAppPool -Name $AppPoolName

$healthStatus = 'NotRun'
$healthBody = ''
try {
    $health = Invoke-WebRequest -Uri "http://localhost:$Port/health" -UseBasicParsing -TimeoutSec 20
    $healthBody = $health.Content
    $parsed = $health.Content | ConvertFrom-Json
    if ($parsed.status -eq 'Healthy') { $healthStatus = 'Healthy' } else { $healthStatus = "Unhealthy: $($parsed.status)" }
} catch {
    $healthStatus = 'Failed'
    $healthBody = $_.Exception.Message
}
Write-ArkanumReport -ReportPath $reportPath -HealthStatus $healthStatus -HealthBody $healthBody -BackupPath $backupPath

if ($healthStatus -ne 'Healthy') {
    Write-Warning "Arkanum installed but health validation did not pass. Report: $reportPath"
    Write-Warning "Rollback app files from backup if needed: $backupPath"
    Write-Warning "Rollback app files example: Copy-Item -Path '$backupPath\app\*' -Destination '$InstallPath' -Recurse -Force; Restart-WebAppPool -Name '$AppPoolName'"
    # Rollback app files is intentionally operator-controlled because database rollback requires a matching SQL backup restore.
}

Write-Host "Arkanum $Action completed for version $PackageVersion."
Write-Host "State manifest: $script:StatePath"
Write-Host "Install report: $reportPath"
Write-Host "Run installers\windows\Test-ArkanumInstall.ps1 -Url http://localhost:$Port/health"
Write-Host "Back up the SQL database and the site-wide recovery/master key together: $keyPath"
