param(
    [string]$PackagePath = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$InstallPath = 'C:\Program Files\Arkanum\Web',
    [string]$ProgramDataPath = 'C:\ProgramData\Arkanum',
    [string]$SiteName = 'Arkanum',
    [int]$Port = 5068,
    [Parameter(Mandatory=$true)][string]$SqlConnectionString,
    [string]$AppPoolName = 'ArkanumAppPool'
)

$ErrorActionPreference = 'Stop'

Import-Module WebAdministration

$appDll = Join-Path $PackagePath 'Arkanum.Web.dll'
if (!(Test-Path $appDll)) {
    throw "Published Arkanum.Web.dll was not found in PackagePath=$PackagePath. Run this from a published release package or pass -PackagePath."
}

$secretDir = Join-Path $ProgramDataPath 'secrets'
$configDir = Join-Path $ProgramDataPath 'config'
$logDir = Join-Path $ProgramDataPath 'logs'
New-Item -ItemType Directory -Force -Path $InstallPath, $secretDir, $configDir, $logDir | Out-Null

Copy-Item -Path (Join-Path $PackagePath '*') -Destination $InstallPath -Recurse -Force

$keyPath = Join-Path $secretDir 'master-key.txt'
if (!(Test-Path $keyPath) -or [string]::IsNullOrWhiteSpace((Get-Content $keyPath -Raw))) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    [Convert]::ToBase64String($bytes) | Set-Content -Path $keyPath -NoNewline
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

# IIS environment variables require the ASP.NET Core module. These are stored in web.config for the app.
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
# Replace embedded null placeholders used only to avoid PowerShell escape confusion.
$webConfig = $webConfig.Replace([char]0, '')
Set-Content -Path (Join-Path $InstallPath 'web.config') -Value $webConfig -Encoding UTF8

if (!(Test-Path "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -Port $Port -PhysicalPath $InstallPath -ApplicationPool $AppPoolName | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $InstallPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}

Restart-WebAppPool -Name $AppPoolName
Write-Host "Arkanum IIS site installed. Run installers\windows\Test-ArkanumInstall.ps1 -Url http://localhost:$Port/health"
