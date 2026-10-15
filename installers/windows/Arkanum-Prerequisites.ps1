#requires -Version 5.1
# Helpers are side-effect free when dot-sourced; native boundaries are mockable.
function Assert-ArkanumWindowsHost {
    param([string]$Edition = $PSVersionTable.PSEdition, [version]$Version = $PSVersionTable.PSVersion,
        [Alias('IsWindows')][bool]$WindowsHost = ($env:OS -eq 'Windows_NT'), [bool]$Is64Bit = [Environment]::Is64BitProcess,
        [bool]$IsAdministrator = $false)
    if (!$PSBoundParameters.ContainsKey('IsAdministrator') -and $WindowsHost) {
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        $IsAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)
    }
    if (!$WindowsHost -or $Edition -ne 'Desktop' -or $Version.Major -ne 5 -or $Version.Minor -lt 1 -or !$Is64Bit -or !$IsAdministrator) {
        throw 'Run elevated 64-bit Windows PowerShell 5.1 Desktop (powershell.exe), not PowerShell Core or x86.'
    }
}
function New-ArkanumRandomBytes {
    param([int]$Count)
    $bytes = New-Object byte[] $Count
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ,$bytes
}
function New-ArkanumSqlConnection {
    param([string]$Server, [string]$Database, [string]$User, [string]$Password, [bool]$Encrypt = $true, [bool]$TrustCertificate = $false)
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = $Database
    $builder['User ID'] = $User
    $builder['Password'] = $Password
    $builder['Encrypt'] = $Encrypt
    $builder['TrustServerCertificate'] = $TrustCertificate
    return $builder.ConnectionString
}
function Assert-ArkanumHostingBundle {
    param([string]$Path, [string]$Sha512)
    if ($Sha512 -notmatch '^[a-fA-F0-9]{128}$' -or (Get-FileHash -LiteralPath $Path -Algorithm SHA512).Hash -ne $Sha512) { throw 'Hosting Bundle SHA512 verification failed.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') { throw 'Hosting Bundle must have a trusted Microsoft Authenticode signature.' }
}
function Test-ArkanumRuntimeVersion {
    param([string[]]$Runtimes, [version]$RequiredVersion)
    foreach ($framework in @('Microsoft.NETCore.App','Microsoft.AspNetCore.App')) {
        $found = $false
        foreach ($runtime in $Runtimes) {
            if ($runtime -match ('^' + [regex]::Escape($framework) + ' (\d+\.\d+\.\d+) \[')) {
                $installed = [version]$Matches[1]
                if ($installed.Major -eq $RequiredVersion.Major -and $installed.Minor -eq $RequiredVersion.Minor -and $installed -ge $RequiredVersion) { $found = $true }
            }
        }
        if (!$found) { return $false }
    }
    return $true
}
function Get-ArkanumPrerequisitePlan {
    param([string]$PackageRoot)
    $manifestPath = Join-Path $PackageRoot 'prerequisites\hosting-bundle.json'
    if (!(Test-Path -LiteralPath $manifestPath)) { throw 'Missing prerequisites/hosting-bundle.json. Use the complete offline Windows package.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.version -notmatch '^10\.0\.\d+$' -or $manifest.fileName -cne "dotnet-hosting-$($manifest.version)-win.exe" -or $manifest.sha512 -notmatch '^[a-fA-F0-9]{128}$' -or $manifest.downloadUrl -notmatch '^https://') { throw 'Invalid Hosting Bundle manifest.' }
    $server = (Get-CimInstance Win32_OperatingSystem).ProductType -ne 1
    if ($server) {
        Import-Module ServerManager
        $required = @('Web-Server','Web-Static-Content','Web-Default-Doc','Web-Http-Errors','Web-Http-Logging','Web-Filtering','Web-Mgmt-Console','Web-Scripting-Tools')
        $missing = @($required | Where-Object { !(Get-WindowsFeature -Name $_).Installed })
    } else {
        $required = @('IIS-WebServerRole','IIS-WebServer','IIS-CommonHttpFeatures','IIS-StaticContent','IIS-DefaultDocument','IIS-HttpErrors','IIS-HttpLogging','IIS-RequestFiltering','IIS-ManagementConsole','IIS-ManagementScriptingTools')
        $missing = @($required | Where-Object { (Get-WindowsOptionalFeature -Online -FeatureName $_).State -ne 'Enabled' })
    }
    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $runtimes = @()
    if (Test-Path -LiteralPath $dotnet) { $runtimes = @(& $dotnet --list-runtimes); if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect installed .NET runtimes.' } }
    $runtimeOk = Test-ArkanumRuntimeVersion -Runtimes $runtimes -RequiredVersion ([version]$manifest.version)
    $ancm = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
    [pscustomobject]@{MissingFeatures=$missing; NeedsHosting=(!$runtimeOk -or !(Test-Path -LiteralPath $ancm) -or $missing.Count -gt 0); BundlePath=(Join-Path $PackageRoot ('prerequisites\' + $manifest.fileName)); Sha512=$manifest.sha512; Server=$server; Dotnet=$dotnet}
}
function Enable-ArkanumIis {
    param($Plan, [string]$Source)
    if ($Plan.Server) {
        $args = @{Name=$Plan.MissingFeatures; IncludeManagementTools=$true; Restart=$false}
        if ($Source) { $args.Source=$Source }
        $result = Install-WindowsFeature @args
        if (!$result.Success) { throw 'IIS feature installation failed. Supply -WindowsFeatureSource if Windows servicing cannot find payloads.' }
        return ($result.RestartNeeded -eq 'Yes')
    }
    $args = @{Online=$true; FeatureName=$Plan.MissingFeatures; All=$true; NoRestart=$true}
    if ($Source) { $args.Source=$Source; $args.LimitAccess=$true }
    $result = Enable-WindowsOptionalFeature @args
    return (@($result | Where-Object RestartNeeded).Count -gt 0)
}
function Stop-ArkanumForRestart {
    param([int]$Code)
    $exception = New-Object System.InvalidOperationException('Prerequisite restart required. Restart Windows in an approved maintenance window, then rerun the same installer command. No app files were changed.')
    $exception.Data['ArkanumExitCode']=$Code
    throw $exception
}
function Invoke-ArkanumPrerequisites {
    param($Plan, [switch]$EnableIisFeatures, [switch]$InstallHostingBundle, [string]$WindowsFeatureSource, [switch]$AllowIisServiceInterruption)
    if (@($Plan.MissingFeatures).Count -gt 0 -and !$EnableIisFeatures) { throw 'IIS features are missing. Explicit consent required: -EnableIisFeatures.' }
    if ($Plan.NeedsHosting -and !$InstallHostingBundle) { throw 'Hosting Bundle installation/repair required. Explicit consent required: -InstallHostingBundle.' }
    if ($Plan.NeedsHosting -and !$AllowIisServiceInterruption -and @(Get-Service -Name WAS,W3SVC -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Running' }).Count -gt 0) {
        throw 'Hosting Bundle may stop/restart all IIS sites despite /norestart. Schedule a maintenance window and explicitly pass -AllowIisServiceInterruption.'
    }
    if ($Plan.NeedsHosting) { Assert-ArkanumHostingBundle -Path $Plan.BundlePath -Sha512 $Plan.Sha512 }
    if (@($Plan.MissingFeatures).Count -gt 0) {
        if (Enable-ArkanumIis -Plan $Plan -Source $WindowsFeatureSource) { Stop-ArkanumForRestart 3010 }
    }
    if ($Plan.NeedsHosting) {
        Assert-ArkanumHostingBundle -Path $Plan.BundlePath -Sha512 $Plan.Sha512
        $process = Start-Process -FilePath $Plan.BundlePath -ArgumentList @('/install','/quiet','/norestart','OPT_NO_X86=1','OPT_NO_ANCM=0','OPT_NO_RUNTIME=0','OPT_NO_SHAREDFX=0') -Wait -PassThru
        if ($process.ExitCode -in @(3010,1641)) { Stop-ArkanumForRestart $process.ExitCode }
        if ($process.ExitCode -ne 0) { throw "Hosting Bundle failed with exit code $($process.ExitCode)." }
        # No explicit iisreset; Microsoft installer may still stop/restart IIS.
        Write-Warning 'Hosting Bundle changed and may have interrupted IIS despite /norestart. A manually scheduled IIS/server restart may still be required.'
    }
}
function Invoke-ArkanumDatabaseCommand {
    param([string]$AppDll, [ValidateSet('provision','preflight')][string]$Mode,
        [string]$SetupConnection, [string]$RuntimeConnection, [string]$RuntimeLogin,
        [string]$RuntimePassword, [string]$MasterKeyFile, [string]$DotnetPath = 'dotnet')
    $values = @{ARKANUM_SETUP_CONNECTION=$SetupConnection; ARKANUM_RUNTIME_CONNECTION=$RuntimeConnection; ARKANUM_RUNTIME_LOGIN=$RuntimeLogin; ARKANUM_RUNTIME_PASSWORD=$RuntimePassword; ARKANUM_MASTER_KEY_FILE=$MasterKeyFile}
    $previous = @{}
    # Do not let an inherited inline key override the explicit scoped key file.
    $values.ARKANUM_MASTER_KEY=$null
    foreach ($name in $values.Keys) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
    try {
        foreach ($name in $values.Keys) { [Environment]::SetEnvironmentVariable($name,$values[$name],'Process') }
        $global:LASTEXITCODE=0
        # Suppress native output: SQL/provider diagnostics can contain credentials.
        & $DotnetPath $AppDll --database-setup $Mode *> $null
        $code = $LASTEXITCODE
        if ($code -ne 0) { throw "Database $Mode failed (exit $code). Installation stopped before app replacement. Check connectivity, schema, permissions, and recovery-key sentinel using the database setup runbook." }
    } catch { throw "Database $Mode failed. Installation stopped before app replacement; no native diagnostic output is displayed because it may contain secrets." }
    finally {
        foreach ($name in $values.Keys) {
            if ($null -eq $previous[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
        }
    }
}
