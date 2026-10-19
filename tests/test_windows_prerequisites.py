"""Windows installer contracts plus executable PowerShell helper tests on Linux.
Run: python3 -m unittest discover -s tests -p test_windows_prerequisites.py -v
"""
import pathlib
import shutil
import subprocess
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
WINDOWS = ROOT / 'installers/windows'

class WindowsPrerequisitesTests(unittest.TestCase):
    def ps(self, body):
        if not shutil.which('pwsh'):
            self.skipTest('pwsh required for parser and mocked helper execution')
        helper = WINDOWS / 'Arkanum-Prerequisites.ps1'
        result = subprocess.run(['pwsh', '-NoLogo', '-NoProfile', '-Command',
                                 "$ErrorActionPreference='Stop'; try { . '" + str(helper) + "'; " + body + "; exit 0 } catch { Write-Error ($_ | Out-String) -ErrorAction Continue; exit 1 }"],
                                text=True, capture_output=True, timeout=40)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout

    def test_all_scripts_parse(self):
        self.ps("Get-ChildItem '" + str(WINDOWS) + "' -Filter *.ps1 | ForEach-Object { $t=$null; $e=$null; [void][System.Management.Automation.Language.Parser]::ParseFile($_.FullName,[ref]$t,[ref]$e); if($e){throw ($e | Out-String)} }")

    def test_desktop_x64_admin_guard_precedes_mutation(self):
        for name in ('Install-Arkanum.ps1', 'Install-ArkanumGui.ps1'):
            text = (WINDOWS / name).read_text()
            self.assertIn('Assert-ArkanumWindowsHost', text)
            self.assertLess(text.index('Assert-ArkanumWindowsHost'), text.find('Import-Module') if 'Import-Module' in text else text.index('Add-Type'))
        self.ps("foreach($edition in @('Core','Desktop')) { try { Assert-ArkanumWindowsHost -Edition $edition -Version ([version]'5.1') -IsWindows $true -Is64Bit $false -IsAdministrator $true; throw 'accepted invalid host' } catch { if($_ -like '*accepted invalid*'){throw} } }; Assert-ArkanumWindowsHost -Edition Desktop -Version ([version]'5.1') -IsWindows $true -Is64Bit $true -IsAdministrator $true")

    def test_rng_uses_framework_api(self):
        text = (WINDOWS / 'Install-Arkanum.ps1').read_text()
        self.assertNotIn('::Fill(', text)
        self.ps("$b=New-ArkanumRandomBytes 32; if($b.Length -ne 32){throw 'bad length'}; $c=New-ArkanumRandomBytes 32; if([Convert]::ToBase64String($b) -eq [Convert]::ToBase64String($c)){throw 'not random'}")

    def test_runtime_detection_requires_pinned_patch_and_both_frameworks(self):
        self.ps("""
        $required=[version]'10.0.13'
        foreach($list in @(@('Microsoft.NETCore.App 10.0.1 [path]','Microsoft.AspNetCore.App 10.0.1 [path]'), @('Microsoft.NETCore.App 10.0.13 [path]'))) {
            if(Test-ArkanumRuntimeVersion -Runtimes $list -RequiredVersion $required){throw 'accepted incomplete or obsolete runtime'}
        }
        if(!(Test-ArkanumRuntimeVersion -Runtimes @('Microsoft.NETCore.App 10.0.14 [path]','Microsoft.AspNetCore.App 10.0.13 [path]') -RequiredVersion $required)){throw 'rejected patched runtime'}
        if(Test-ArkanumRuntimeVersion -Runtimes @('Microsoft.NETCore.App 11.0.0 [path]','Microsoft.AspNetCore.App 11.0.0 [path]') -RequiredVersion $required){throw 'accepted incompatible major'}
        """)

    def test_hash_and_microsoft_signature_are_both_required(self):
        self.ps("""
        function Get-FileHash { [pscustomobject]@{Hash=('a'*128)} }
        function Get-AuthenticodeSignature { [pscustomobject]@{Status='Valid'; SignerCertificate=[pscustomobject]@{Subject='CN=Microsoft Corporation, O=Microsoft Corporation, C=US'}} }
        Assert-ArkanumHostingBundle -Path ignored -Sha512 ('a'*128)
        try { Assert-ArkanumHostingBundle -Path ignored -Sha512 ('b'*128); throw 'accepted bad hash' } catch {if($_ -like '*accepted bad*'){throw}}
        function Get-AuthenticodeSignature { [pscustomobject]@{Status='Valid'; SignerCertificate=[pscustomobject]@{Subject='CN=Not Microsoft, O=Attacker'}} }
        try { Assert-ArkanumHostingBundle -Path ignored -Sha512 ('a'*128); throw 'accepted bad publisher' } catch {if($_ -like '*accepted bad*'){throw}}
        function Get-AuthenticodeSignature { [pscustomobject]@{Status='NotTrusted'; SignerCertificate=[pscustomobject]@{Subject='O=Microsoft Corporation'}} }
        try { Assert-ArkanumHostingBundle -Path ignored -Sha512 ('a'*128); throw 'accepted bad trust' } catch {if($_ -like '*accepted bad*'){throw}}
        """)

    def test_prerequisite_consent_order_and_reboot(self):
        self.ps("""
        $plan=[pscustomobject]@{MissingFeatures=@('IIS-WebServerRole'); NeedsHosting=$true; BundlePath='bundle.exe'; Sha512=('a'*128); Server=$false}
        $script:events=@()
        function Enable-ArkanumIis {param($Plan,$Source) $script:events+='iis'; return $false}
        function Get-Service { @() }
        function Assert-ArkanumHostingBundle {param($Path,$Sha512) $script:events+='verify'}
        function Start-Process {param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru) $script:events+='hosting'; [pscustomobject]@{ExitCode=0}}
        try { Invoke-ArkanumPrerequisites -Plan $plan; throw 'consent bypass' } catch {if($_ -like '*consent bypass*'){throw}}
        if($events.Count){throw 'mutation before consent'}
        Invoke-ArkanumPrerequisites -Plan $plan -EnableIisFeatures -InstallHostingBundle
        if(($events -join ',') -ne 'verify,iis,verify,hosting'){throw ($events -join ',')}
        foreach($code in @(3010,1641)) {
            function Start-Process {param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru) [pscustomobject]@{ExitCode=$code}}
            try { Invoke-ArkanumPrerequisites -Plan $plan -EnableIisFeatures -InstallHostingBundle; throw 'reboot ignored' } catch { if($_ -like '*reboot ignored*'){throw}; if($_.Exception.Data['ArkanumExitCode'] -ne $code){throw 'missing reboot code'} }
        }
        function Enable-ArkanumIis {param($Plan,$Source) return $true}
        $script:events=@()
        try { Invoke-ArkanumPrerequisites -Plan $plan -EnableIisFeatures -InstallHostingBundle; throw 'IIS reboot ignored' } catch {if($_ -like '*IIS reboot ignored*'){throw}}
        if($events -contains 'hosting'){throw 'hosting ran before IIS reboot'}
        """)

    def test_database_environment_restored_and_native_failure_redacted(self):
        self.ps("""
        $env:ARKANUM_SETUP_CONNECTION='old'; $env:ARKANUM_MASTER_KEY='oldkey'
        function dotnet { $script:seen=$env:ARKANUM_SETUP_CONNECTION; $script:key=$env:ARKANUM_MASTER_KEY_FILE; $global:LASTEXITCODE=23; 'secret failure text' }
        try { Invoke-ArkanumDatabaseCommand -AppDll app.dll -Mode preflight -SetupConnection secret -RuntimeConnection runtime -RuntimeLogin login -RuntimePassword password -MasterKeyFile recovery; throw 'failure ignored' } catch {if($_ -like '*failure ignored*' -or $_ -like '*secret failure text*'){throw}}
        if($script:seen -ne 'secret' -or $script:key -ne 'recovery'){throw 'env not passed'}
        if($env:ARKANUM_SETUP_CONNECTION -ne 'old' -or $env:ARKANUM_MASTER_KEY -ne 'oldkey' -or $null -ne $env:ARKANUM_RUNTIME_PASSWORD){throw 'env leaked'}
        """)

    def test_installer_contract_and_readonly_preflight(self):
        text = (WINDOWS / 'Install-Arkanum.ps1').read_text()
        self.assertNotIn('& sqlcmd', text)
        self.assertIn('Invoke-ArkanumDatabaseCommand', text)
        self.assertIn('setup\\arkanum-schema.sql', text)
        self.assertLess(text.index('if ($PreflightOnly)'), text.index('Invoke-ArkanumPrerequisites -Plan'))
        self.assertLess(text.index('RestoreKeyFile does not exist'), text.index('Invoke-ArkanumPrerequisites -Plan'))
        self.assertLess(text.index('HttpsCertificateThumbprint is required'), text.index('Invoke-ArkanumPrerequisites -Plan'))
        self.assertNotIn("$recoveryValidation = 'Passed'", text)
        self.assertIn("$parsed.status -eq 'Alive'", text)
        self.assertNotIn('provided Recovery Key does not match this database', text)

    def test_gui_consent_and_secret_free_preview(self):
        text = (WINDOWS / 'Install-ArkanumGui.ps1').read_text()
        self.assertIn('EnableIisFeatures = $enableIis.Checked', text)
        self.assertIn('InstallHostingBundle = $installHosting.Checked', text)
        preview = text[text.index('function Build-CommandPreview'):text.index('$generate =')]
        self.assertNotIn('$setupSqlPassword.Text', preview)
        self.assertNotIn('$runtimeSqlPassword.Text', preview)
        self.assertNotIn('$restoreKey.Text', preview)
        self.assertNotIn("$args -join", preview)

    def test_running_iis_requires_separate_interruption_consent(self):
        self.ps("""
        $plan=[pscustomobject]@{MissingFeatures=@(); NeedsHosting=$true; BundlePath='bundle.exe'; Sha512=('a'*128); Server=$false}
        function Get-Service { [pscustomobject]@{Status='Running'} }
        function Assert-ArkanumHostingBundle {param($Path,$Sha512)}
        $script:launched=$false
        function Start-Process {param($FilePath,$ArgumentList,[switch]$Wait,[switch]$PassThru) $script:launched=$true; [pscustomobject]@{ExitCode=0}}
        try { Invoke-ArkanumPrerequisites -Plan $plan -InstallHostingBundle; throw 'interruption bypass' } catch {if($_ -like '*interruption bypass*'){throw}}
        if($launched){throw 'launched without consent'}
        Invoke-ArkanumPrerequisites -Plan $plan -InstallHostingBundle -AllowIisServiceInterruption
        if(!$launched){throw 'did not launch'}
        """)

    def test_liveness_script_accepts_alive_without_sentinel_claim(self):
        text = (WINDOWS / 'Test-ArkanumInstall.ps1').read_text()
        self.assertIn("$response.status -ne 'Alive'", text)
        self.assertIn('not Recovery Key', text)

    def test_sql_builder_roundtrips_special_password(self):
        self.ps("""
        $password='semi;quote"''=slash\\space '
        $connection=New-ArkanumSqlConnection -Server 'tcp:db,1433' -Database Arkanum -User runtime -Password $password
        $builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($connection)
        if($builder.Password -cne $password -or $builder.UserID -ne 'runtime' -or !$builder.Encrypt -or $builder.TrustServerCertificate){throw 'unsafe connection string'}
        """)

    def test_helper_success_restores_every_environment_field(self):
        self.ps("""
        $names=@('ARKANUM_SETUP_CONNECTION','ARKANUM_RUNTIME_CONNECTION','ARKANUM_RUNTIME_LOGIN','ARKANUM_RUNTIME_PASSWORD','ARKANUM_MASTER_KEY_FILE','ARKANUM_MASTER_KEY')
        foreach($name in $names){[Environment]::SetEnvironmentVariable($name,'previous','Process')}
        function dotnet {
            if($args[0] -ne 'bundled/app.dll' -or $args[1] -ne '--database-setup' -or $args[2] -ne 'provision'){throw 'wrong command'}
            if($env:ARKANUM_RUNTIME_CONNECTION -ne 'runtime' -or $env:ARKANUM_RUNTIME_PASSWORD -ne 'password' -or $env:ARKANUM_MASTER_KEY_FILE -ne 'key.txt' -or $env:ARKANUM_MASTER_KEY){throw 'wrong environment'}
            $global:LASTEXITCODE=0
        }
        Invoke-ArkanumDatabaseCommand -AppDll bundled/app.dll -Mode provision -SetupConnection setup -RuntimeConnection runtime -RuntimeLogin login -RuntimePassword password -MasterKeyFile key.txt
        foreach($name in $names){if([Environment]::GetEnvironmentVariable($name,'Process') -ne 'previous'){throw 'not restored'}}
        """)

    def test_installer_readonly_and_database_failure_precede_mutations(self):
        # Execute the real installer body, substituting only host/native boundaries.
        self.ps("""
        $source=Get-Content '""" + str(WINDOWS / 'Install-Arkanum.ps1') + """' -Raw
        $source=$source.Replace('. (Join-Path $PSScriptRoot ''Arkanum-Prerequisites.ps1'')','')
        . '""" + str(WINDOWS / 'Arkanum-Version.ps1') + """'
        $source=$source.Replace('. (Join-Path $PSScriptRoot ''Arkanum-Version.ps1'')','')
        $source=$source.Replace("`nAssert-ArkanumWindowsHost`n","`n")
        function Test-Path { param($LiteralPath,$Path) $p=if($LiteralPath){$LiteralPath}else{$Path}; return ($p -match '^/package/Arkanum.Web.(dll|runtimeconfig.json)$') }
        function Get-Item { [pscustomobject]@{Thumbprint='certificate'} }
        function Get-ArkanumPrerequisitePlan { [pscustomobject]@{MissingFeatures=@(); NeedsHosting=$false; Dotnet='dotnet'} }
        function Import-Module {}
        function New-Item { throw 'MUTATION forbidden' }
        function Copy-Item { throw 'MUTATION forbidden' }
        function Stop-WebAppPool { throw 'MUTATION forbidden' }
        $script:modes=@()
        function Invoke-ArkanumDatabaseCommand {param($DotnetPath,$AppDll,$Mode,$RuntimeConnection,$MasterKeyFile,$SetupConnection,$RuntimeLogin,$RuntimePassword) $script:modes+=$Mode; if($script:fail){throw 'database failed'} }
        $parameters=@{PackagePath='/package';InstallPath='/absent';ProgramDataPath='/data';HttpsCertificateThumbprint='certificate';SqlConnectionString='Server=db;Database=Arkanum;User Id=runtime;Password=secret';Action='install'}
        & ([scriptblock]::Create($source)) @parameters -PreflightOnly
        if(($modes -join ',') -ne 'preflight'){throw 'read-only did not preflight'}
        $parameters.SqlConnectionString='NotAKeyword=secret'
        try { & ([scriptblock]::Create($source)) @parameters -PreflightOnly; throw 'accepted invalid connection' } catch {if($_ -like '*accepted invalid connection*'){throw}}
        $parameters.SqlConnectionString='Server=db;Database=Arkanum;User Id=runtime;Password=secret'
        $script:fail=$true
        try { & ([scriptblock]::Create($source)) @parameters; throw 'failure ignored' } catch {if($_ -like '*MUTATION*' -or $_ -like '*failure ignored*'){throw}}
        """)

if __name__ == '__main__':
    unittest.main()
