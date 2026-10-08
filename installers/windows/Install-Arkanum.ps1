param(
    [string]$PackagePath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$InstallPath = 'C:\Program Files\Arkanum\Web',
    [string]$ProgramDataPath = 'C:\ProgramData\Arkanum',
    [string]$SiteName = 'Arkanum',
    [int]$Port = 443,
    [string]$HttpsHostName = '',
    [string]$HttpsCertificateThumbprint = '',
    [string]$SqlConnectionString = '',
    [string]$AppPoolName = 'ArkanumAppPool',
    [string]$PackageVersion = '2026.1.1',
    [ValidateSet('full','update','auto')][string]$PackageKind = 'auto',
    [switch]$PreflightOnly,
    # ValidateSet('install','upgrade','repair','side-by-side','restore') plus auto detection for GUI defaults.
    [ValidateSet('install','upgrade','repair','side-by-side','restore','auto')][string]$Action = 'auto',
    [ValidateSet('auto','use-existing-database','create-new-database','restore-database')][string]$DatabaseSetupMode = 'auto',
    [string]$SqlServerHost = '',
    [string]$SqlServerPort = '1433',
    [string]$DatabaseName = 'Arkanum',
    [string]$SetupSqlUsername = '',
    [string]$SetupSqlPassword = '',
    [string]$RuntimeSqlLogin = 'arkanum_app',
    [string]$RuntimeSqlPassword = '',
    [switch]$GenerateRuntimePassword,
    [ValidateSet('dba-restored-database','sql-backup-file','attach-database-files')][string]$RestoreSource = 'dba-restored-database',
    [string]$SqlBackupFile = '',
    [string]$RestoreKeyFile = '',
    [string]$RestoreKey = ''
)

$ErrorActionPreference = 'Stop'

function Write-ArkanumReport {
    param(
        [string]$ReportPath,
        [string]$HealthStatus,
        [string]$HealthBody,
        [string]$BackupPath,
        [string]$RecoveryValidation = 'NotRun'
    )

    $restoreKeySource = if (![string]::IsNullOrWhiteSpace($RestoreKeyFile)) { 'file' } elseif (![string]::IsNullOrWhiteSpace($RestoreKey)) { 'pasted' } else { 'not provided' }

    $lines = @(
        'Arkanum installer report',
        'Restore report',
        'Product: Arkanum',
        "Package version: $PackageVersion",
        "Install action: $Action",
        "Install path: $InstallPath",
        "ProgramData path: $ProgramDataPath",
        "Site name: $SiteName",
        "App pool name: $AppPoolName",
        "State file: $script:StatePath",
        "Generated UTC: $((Get-Date).ToUniversalTime().ToString('s'))Z",
        "Health URL: https://$($script:HealthHost):$Port/health",
        'HTTPS binding: required for production; configure IIS TLS 1.2/1.3 and a trusted server certificate.',
        "Backup path: $BackupPath",
        "Database setup mode: $DatabaseSetupMode",
        "Runtime SQL login: $RuntimeSqlLogin",
        'SQL setup credential persistence: not persisted',
        'setup/admin SQL credential is used only during provisioning.',
        "Restore source: $RestoreSource",
        "SQL backup file: $(if ([string]::IsNullOrWhiteSpace($SqlBackupFile)) { 'not provided' } else { $SqlBackupFile })",
        "Recovery Key source: $restoreKeySource",
        "Recovery sentinel validation: $RecoveryValidation",
        'No SQL passwords or Recovery Key values are written to this report.',
        "Post-install health status: $HealthStatus",
        (Redact-ArkanumSecret $HealthBody)
    )
    $lines | Set-Content -Path $ReportPath -Encoding UTF8
}


function New-ArkanumRuntimePassword {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes).TrimEnd('=') + '!aA1'
}

function Redact-ArkanumSecret {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return $Text }
    $redacted = $Text
    foreach ($secret in @($SetupSqlPassword, $RuntimeSqlPassword, $SqlConnectionString, $RestoreKey)) {
        if (![string]::IsNullOrWhiteSpace($secret)) { $redacted = $redacted -replace [regex]::Escape($secret), '[REDACTED]' }
    }
    return ($redacted -replace '(Password=)[^;]+', '$1[REDACTED]')
}

function Protect-ArkanumSecretFile {
    param([string]$Path)
    $acl = Get-Acl $Path
    $acl.SetAccessRuleProtection($true, $false)
    $acl.Access | ForEach-Object { $acl.RemoveAccessRule($_) | Out-Null }
    $appPoolIdentity = "IIS AppPool\$AppPoolName"
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule('Administrators', 'FullControl', 'Allow')))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule('SYSTEM', 'FullControl', 'Allow')))
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($appPoolIdentity, 'Read', 'Allow')))
    Set-Acl -Path $Path -AclObject $acl
}

function Build-ArkanumRuntimeSqlConnectionString {
    if (![string]::IsNullOrWhiteSpace($SqlConnectionString)) { return $SqlConnectionString }
    if ([string]::IsNullOrWhiteSpace($SqlServerHost) -or [string]::IsNullOrWhiteSpace($DatabaseName) -or [string]::IsNullOrWhiteSpace($RuntimeSqlLogin) -or [string]::IsNullOrWhiteSpace($RuntimeSqlPassword)) {
        throw 'SqlConnectionString or SQL Server host/database/runtime login/runtime password is required.'
    }
    return "Server=tcp:$SqlServerHost,$SqlServerPort;Database=$DatabaseName;User Id=$RuntimeSqlLogin;Password=$RuntimeSqlPassword;Encrypt=True;TrustServerCertificate=False;"
}

function Invoke-ArkanumSqlProvisioning {
    if ($DatabaseSetupMode -ne 'create-new-database') { return }
    if ([string]::IsNullOrWhiteSpace($SetupSqlUsername) -or [string]::IsNullOrWhiteSpace($SetupSqlPassword)) {
        throw 'Create new database mode requires setup/admin SQL username and password. The setup/admin SQL credential is used only during provisioning and is not persisted.'
    }
    if ([string]::IsNullOrWhiteSpace($RuntimeSqlPassword)) { throw 'Create new database mode requires a runtime SQL password or -GenerateRuntimePassword.' }
    $scriptPath = Join-Path $PSScriptRoot '..\sql\provision-arkanum-sql.sql'
    if (!(Test-Path $scriptPath)) { throw "Missing SQL provisioning script: $scriptPath" }
    $schemaPath = Join-Path $PSScriptRoot '..\sql\arkanum-schema.sql'
    if (!(Test-Path $schemaPath)) { throw "Missing SQL schema script: $schemaPath" }
    $server = "tcp:$SqlServerHost,$SqlServerPort"
    Write-Host 'Database setup mode: create-new-database'
    Write-Host 'Creating Arkanum database and limited runtime SQL login/user. SQL setup credential persistence: not persisted.'
    Write-Host 'Persist only the runtime SQL connection after provisioning.'
    & sqlcmd -S $server -U $SetupSqlUsername -P $SetupSqlPassword -b -i $schemaPath -v DatabaseName=$DatabaseName | ForEach-Object { Redact-ArkanumSecret $_ }
    & sqlcmd -S $server -U $SetupSqlUsername -P $SetupSqlPassword -b -i $scriptPath -v DatabaseName=$DatabaseName RuntimeLogin=$RuntimeSqlLogin RuntimePassword=$RuntimeSqlPassword | ForEach-Object { Redact-ArkanumSecret $_ }
}

function Write-ArkanumPackagePreflight {
    param([string]$InstalledVersion, [string]$BackupPathPreview)
    Write-Host 'Arkanum update preflight'
    Write-Host "Current version: $InstalledVersion"
    Write-Host "Target version: $PackageVersion"
    Write-Host "Package kind: $PackageKind"
    Write-Host 'Update package will preserve existing configuration, Recovery Key, database connection, and service binding settings.'
    Write-Host "Backup destination: $BackupPathPreview"
    Write-Host 'No SQL passwords or Recovery Key values are written to this report.'
}

function Invoke-ArkanumSqlUpgradePreflight {
    if ($Action -ne 'upgrade') { return }
    $scriptPath = Join-Path $PSScriptRoot '..\sql\upgrade-preflight.sql'
    if (!(Test-Path $scriptPath)) { throw "Missing SQL upgrade preflight script: $scriptPath" }
    Write-Host 'Database reachability preflight'
    Write-Host 'Schema/version preflight'
    Write-Host 'Runtime login permission preflight'
    Write-Host 'SQL backup strongly recommended before upgrade.'
    Write-Host 'Failure categories: database reachability, schema mismatch, permission problem, recovery-key mismatch.'
    & sqlcmd -b -i $scriptPath | ForEach-Object { $_ -replace '(Password=)[^;]+', '$1[REDACTED]' }
}

Import-Module WebAdministration

$manifestPath = Join-Path $PackagePath 'package-manifest.json'
if (Test-Path $manifestPath) {
    try {
        $packageManifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        if ($PackageKind -eq 'auto' -and ![string]::IsNullOrWhiteSpace($packageManifest.packageKind)) { $PackageKind = $packageManifest.packageKind }
        if (![string]::IsNullOrWhiteSpace($packageManifest.version)) { $PackageVersion = $packageManifest.version }
    } catch { throw "Package manifest could not be read: $manifestPath" }
}
if ($PackageKind -eq 'auto') { $PackageKind = 'full' }

$appDll = Join-Path $PackagePath 'Arkanum.Web.dll'
if (!(Test-Path $appDll) -and (Test-Path (Join-Path $PackagePath 'app\Arkanum.Web.dll'))) {
    $PackagePath = Join-Path $PackagePath 'app'
    $appDll = Join-Path $PackagePath 'Arkanum.Web.dll'
}
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
$existingSqlConnectionPath = Join-Path $secretDir 'sql-connection.txt'
$existingMasterKeyPath = Join-Path $secretDir 'master-key.txt'
$installedVersion = 'unknown'
if (Test-Path $script:StatePath) {
    try { $installedVersion = (Get-Content $script:StatePath -Raw | ConvertFrom-Json).installedVersion } catch { $installedVersion = 'unknown' }
}
if ($PackageKind -eq 'update') {
    if (-not $existingInstall) { throw 'Update package requires an existing Arkanum installation. Use the full package for first-time installation or restore.' }
    if (-not (Test-Path $existingMasterKeyPath)) { throw 'Update package requires existing Recovery Key material. Update stopped before changing files.' }
    if ([string]::IsNullOrWhiteSpace($SqlConnectionString) -and [string]::IsNullOrWhiteSpace($SqlServerHost) -and (Test-Path $existingSqlConnectionPath)) {
        $SqlConnectionString = Get-Content $existingSqlConnectionPath -Raw
    }
    if ([string]::IsNullOrWhiteSpace($SqlConnectionString) -and -not (Test-Path $existingSqlConnectionPath)) { throw 'Update package requires existing database connectivity settings or an explicit replacement connection. Update stopped before changing files.' }
    if ($Action -eq 'auto') { $Action = 'upgrade' }
    if ($Action -ne 'upgrade' -and $Action -ne 'repair') { throw 'Update package can only run upgrade or repair actions against an existing installation.' }
}
if ($Action -eq 'auto') {
    if ($existingInstall) { $Action = 'upgrade' } else { $Action = 'install' }
}

if ($Action -eq 'restore') {
    $reportPath = Join-Path $logDir "restore-report-$PackageVersion.txt"
}

if ($DatabaseSetupMode -eq 'auto') {
    if ($Action -eq 'restore') { $DatabaseSetupMode = 'restore-database' } else { $DatabaseSetupMode = 'use-existing-database' }
}
if ($GenerateRuntimePassword -and [string]::IsNullOrWhiteSpace($RuntimeSqlPassword)) { $RuntimeSqlPassword = New-ArkanumRuntimePassword }
$SqlConnectionString = Build-ArkanumRuntimeSqlConnectionString
if ($PreflightOnly) {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
    $previewBackup = Join-Path $backupRoot "$installedVersion-before-$PackageVersion-$stamp"
    Write-ArkanumPackagePreflight -InstalledVersion $installedVersion -BackupPathPreview $previewBackup
    return
}
if ([string]::IsNullOrWhiteSpace($HttpsCertificateThumbprint)) {
    throw 'HttpsCertificateThumbprint is required for the production HTTPS binding.'
}
$httpsCertificate = Get-Item "Cert:\LocalMachine\My\$HttpsCertificateThumbprint" -ErrorAction SilentlyContinue
if ($null -eq $httpsCertificate) {
    throw "HTTPS certificate was not found in Cert:\LocalMachine\My for thumbprint $HttpsCertificateThumbprint."
}
$script:HealthHost = if ([string]::IsNullOrWhiteSpace($HttpsHostName)) { 'localhost' } else { $HttpsHostName }

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

if ($Action -eq 'restore' -and [string]::IsNullOrWhiteSpace($RestoreKeyFile) -and [string]::IsNullOrWhiteSpace($RestoreKey)) {
    throw 'Restore from database backup requires -RestoreKeyFile or -RestoreKey.'
}

if ($Action -eq 'restore' -and $RestoreSource -ne 'dba-restored-database') {
    throw 'SQL backup and attach restore paths are staged for DBA workflow selection but not automated in this release. For this release, restore the SQL database first, then choose dba-restored-database.'
}

New-Item -ItemType Directory -Force -Path $InstallPath, $secretDir, $configDir, $logDir, $backupRoot | Out-Null
Invoke-ArkanumSqlProvisioning
Invoke-ArkanumSqlUpgradePreflight

$backupPath = ''
if ($Action -in @('upgrade','repair')) {
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
if ($Action -eq 'restore') {
    Write-Host 'Restore/recover existing Arkanum instance selected.'
    Write-Host "Restore source: $RestoreSource"
    Write-Host 'Using database already restored by DBA. Installing provided Recovery Key.'
    if (![string]::IsNullOrWhiteSpace($RestoreKeyFile)) {
        if (!(Test-Path $RestoreKeyFile)) { throw 'RestoreKeyFile does not exist.' }
        Copy-Item -Path $RestoreKeyFile -Destination $keyPath -Force
    } else {
        Set-Content -Path $keyPath -Value $RestoreKey.Trim() -NoNewline
    }
    if (!((Get-Content $keyPath -Raw).Trim().StartsWith('ark-recovery-v1.'))) { throw 'Provided Recovery Key must start with ark-recovery-v1.' }
    Write-Host 'No new Recovery Key was generated during restore mode.'
} elseif (!(Test-Path $keyPath) -or [string]::IsNullOrWhiteSpace((Get-Content $keyPath -Raw))) {
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

$connectionStringPath = Join-Path $secretDir 'sql-connection.txt'
Set-Content -Path $connectionStringPath -Value $SqlConnectionString -NoNewline
Protect-ArkanumSecretFile -Path $keyPath
Protect-ArkanumSecretFile -Path $connectionStringPath

$configPath = Join-Path $configDir 'arkanum.runtime.json'
$runtimeJson = @{
    ASPNETCORE_ENVIRONMENT = 'Production'
    ARKANUM_MASTER_KEY_FILE = $keyPath
    ConnectionStrings__DefaultConnection_FILE = $connectionStringPath
} | ConvertTo-Json
Set-Content -Path $configPath -Value $runtimeJson

$escapedKeyPath = [System.Security.SecurityElement]::Escape($keyPath)
$escapedConnectionStringPath = [System.Security.SecurityElement]::Escape($connectionStringPath)

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
        <environmentVariable name="ARKANUM_MASTER_KEY_FILE" value="$escapedKeyPath" />
        <environmentVariable name="ConnectionStrings__DefaultConnection_FILE" value="$escapedConnectionStringPath" />
      </environmentVariables>
    </aspNetCore>
  </system.webServer>
</configuration>
"@
Set-Content -Path (Join-Path $InstallPath 'web.config') -Value $webConfig -Encoding UTF8

$sslFlags = if ([string]::IsNullOrWhiteSpace($HttpsHostName)) { 0 } else { 1 }
$bindingInformation = if ([string]::IsNullOrWhiteSpace($HttpsHostName)) { "*:$($Port):" } else { "*:$($Port):$HttpsHostName" }
if (!(Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -Port $Port -HostHeader $HttpsHostName -PhysicalPath $InstallPath -ApplicationPool $AppPoolName | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $InstallPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}
$existingHttpBinding = Get-WebBinding -Name $SiteName -Protocol 'http' -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -eq $bindingInformation } | Select-Object -First 1
if ($null -ne $existingHttpBinding) {
    Remove-WebBinding -Name $SiteName -Protocol 'http' -Port $Port -HostHeader $HttpsHostName
}
$existingHttpsBinding = Get-WebBinding -Name $SiteName -Protocol 'https' -ErrorAction SilentlyContinue | Where-Object { $_.bindingInformation -eq $bindingInformation } | Select-Object -First 1
if ($null -eq $existingHttpsBinding) {
    New-WebBinding -Name $SiteName -Protocol 'https' -Port $Port -HostHeader $HttpsHostName -SslFlags $sslFlags | Out-Null
}
$bindingPath = if ([string]::IsNullOrWhiteSpace($HttpsHostName)) { "IIS:\SslBindings\0.0.0.0!$Port" } else { "IIS:\SslBindings\0.0.0.0!$Port!$HttpsHostName" }
if (Test-Path $bindingPath) {
    Remove-Item $bindingPath -Force
}
$httpsCertificate | New-Item $bindingPath -SSLFlags $sslFlags | Out-Null
Set-WebConfigurationProperty -Filter 'system.webServer/security/access' -Name sslFlags -Value 'Ssl' -PSPath "IIS:\Sites\$SiteName"

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
    packageChannel = 'release'
    backupPath = $backupPath
}
$state | ConvertTo-Json | Set-Content -Path $script:StatePath -Encoding UTF8

Restart-WebAppPool -Name $AppPoolName

$healthStatus = 'NotRun'
$healthBody = ''
$recoveryValidation = if ($Action -eq 'restore') { 'Pending health check' } else { 'Not applicable' }
try {
    $health = Invoke-WebRequest -Uri "https://$($script:HealthHost):$Port/health" -UseBasicParsing -TimeoutSec 20
    $healthBody = $health.Content
    $parsed = $health.Content | ConvertFrom-Json
    if ($parsed.status -eq 'Healthy') { $healthStatus = 'Healthy'; if ($Action -eq 'restore') { $recoveryValidation = 'Passed' } } else { $healthStatus = "Unhealthy: $($parsed.status)"; if ($Action -eq 'restore') { $recoveryValidation = 'Failed' } }
} catch {
    $healthStatus = 'Failed'
    $healthBody = $_.Exception.Message
    if ($Action -eq 'restore') { $recoveryValidation = 'Failed' }
}
Write-ArkanumReport -ReportPath $reportPath -HealthStatus $healthStatus -HealthBody $healthBody -BackupPath $backupPath -RecoveryValidation $recoveryValidation

if ($healthStatus -ne 'Healthy') {
    if ($Action -eq 'restore') { throw "The provided Recovery Key does not match this database. The provided Arkanum Recovery Key does not match the restored database. Recoverable state requires both the SQL database backup and the matching Arkanum Recovery Key. No new Recovery Key was generated. Report: $reportPath" }
    Write-Warning "Arkanum installed but health validation did not pass. Report: $reportPath"
    Write-Warning "Rollback app files from backup if needed: $backupPath"
    Write-Warning "Rollback app files example: Copy-Item -Path '$backupPath\app\*' -Destination '$InstallPath' -Recurse -Force; Restart-WebAppPool -Name '$AppPoolName'"
    # Rollback app files is intentionally operator-controlled because database rollback requires a matching SQL backup restore.
}

Write-Host "Arkanum $Action completed for version $PackageVersion."
Write-Host "State manifest: $script:StatePath"
Write-Host "Install report: $reportPath"
Write-Host "Run installers\windows\Test-ArkanumInstall.ps1 -Url https://$($script:HealthHost):$Port/health"
Write-Host "Back up the SQL database and the site-wide recovery/master key together: $keyPath"
