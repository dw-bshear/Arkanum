"""Issue 30: exercise the real platform version policies, not numeric substitutes."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
# Same vectors run against Python and the Windows PowerShell 5.1-compatible policy.
CASES = [
    ('2026.1.2', '1.0.0', 'upgrade', True),
    ('1.0.0', '1.0.0', 'upgrade', True),
    ('1.0.0', '1.0.0', 'repair', True),
    ('1.0.0+old', '1.0.0+new', 'repair', True),
    ('1.0.0', '1.0.1', 'upgrade', True),
    ('1.0.9', '1.1.0', 'upgrade', True),
    ('1.9.9', '2.0.0', 'upgrade', True),
    ('1.0.0-rc.2', '1.0.0-rc.10', 'upgrade', True),
    ('1.0.0-alpha', '1.0.0-alpha.1', 'upgrade', True),
    ('1.0.0-1', '1.0.0-alpha', 'upgrade', True),
    ('1.0.0-rc.1', '1.0.0', 'upgrade', True),
    ('1.0.0', '1.0.0-rc.1', 'upgrade', False),
    ('1.0.1', '1.0.0', 'upgrade', False),
    ('2.0.0', '1.9.9', 'repair', False),
    ('1.0.0', '1.0.1', 'repair', False),
    ('2026.1.2', '1.0.0-rc.1', 'upgrade', False),
    ('1.0.0', '2026.1.2', 'upgrade', False),
    ('2026.1.1', '1.0.0', 'upgrade', False),
    ('unknown', '1.0.0', 'upgrade', False),
    ('2027.1.0', '1.0.0', 'upgrade', False),
    ('1.0', '1.0.0', 'upgrade', False),
    ('01.0.0', '1.0.0', 'upgrade', False),
    ('1.0.0-01', '1.0.0', 'upgrade', False),
    ('1.0.0', '../1.0.0', 'upgrade', False),
    ('1.0.0', '1.0.0\n', 'upgrade', False),
]

class TransitionTests(unittest.TestCase):
    def test_linux_gui_emits_current_version(self):
        text = (ROOT / 'installers/linux/arkanum-gui-install.py').read_text()
        self.assertIn('"ARKANUM_PACKAGE_VERSION": "1.0.0"', text)
        self.assertNotIn('"ARKANUM_PACKAGE_VERSION": "2026.1.2"', text)

    def test_linux_policy(self):
        helper = ROOT / 'installers/linux/arkanum_version.py'
        self.assertTrue(helper.is_file(), 'missing family-aware Linux version policy')
        for source, target, action, accepted in CASES:
            with self.subTest(source=source, target=target, action=action):
                result = subprocess.run(['python3', str(helper), 'check', source, target, action], capture_output=True, text=True)
                self.assertEqual(accepted, result.returncode == 0, result.stderr)

    @unittest.skipUnless(shutil.which('pwsh'), 'PowerShell unavailable; native 5.1 remains a separate gate')
    def test_windows_policy_same_vectors(self):
        helper = ROOT / 'installers/windows/Arkanum-Version.ps1'
        self.assertTrue(helper.is_file(), 'missing family-aware Windows version policy')
        # JSON avoids interpreting version strings as executable PowerShell.
        with tempfile.TemporaryDirectory() as tmp:
            vectors = Path(tmp) / 'cases.json'
            vectors.write_text(json.dumps(CASES))
            script = f". '{helper}'; $cases=Get-Content '{vectors}' -Raw|ConvertFrom-Json; foreach($c in $cases) {{ $ok=$true; try {{ Assert-ArkanumVersionTransition -InstalledVersion $c[0] -PackageVersion $c[1] -Action $c[2] }} catch {{ $ok=$false }}; if($ok -ne $c[3]) {{ throw ('Unexpected decision: '+($c|ConvertTo-Json -Compress)) }} }}"
            result = subprocess.run(['pwsh', '-NoProfile', '-Command', script], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)

    def test_current_version_defaults(self):
        self.assertEqual('1.0.0', ET.parse(ROOT / 'Directory.Build.props').findtext('.//Version'))
        for path in ['installers/windows/Install-Arkanum.ps1', 'installers/windows/Install-ArkanumGui.ps1', 'installers/windows/Build-ArkanumMsi.ps1', 'installers/linux/install-arkanum.sh', 'installers/linux/build-native-package.sh']:
            with self.subTest(path=path):
                text = (ROOT / path).read_text()
                self.assertIn('1.0.0', text)
                self.assertNotIn("'2026.1.2'", text)

    def test_installer_decisions_precede_mutation(self):
        linux = (ROOT / 'installers/linux/install-arkanum.sh').read_text()
        windows = (ROOT / 'installers/windows/Install-Arkanum.ps1').read_text()
        self.assertIn('arkanum_version.py', linux)
        self.assertLess(linux.index('arkanum_version.py'), linux.index('if [ "${PREFLIGHT_ONLY}" = "true" ]'))
        self.assertIn('Assert-ArkanumVersionTransition', windows)
        self.assertLess(windows.index('Assert-ArkanumVersionTransition'), windows.index('$SqlConnectionString = Build-ArkanumRuntimeSqlConnectionString'))

    def test_deb_epoch_survives_calendar_reset_and_stages_outside_live_app(self):
        with tempfile.TemporaryDirectory() as tmp:
            app = Path(tmp) / 'app'; app.mkdir()
            (app / 'Arkanum.Web.dll').write_text('TEST FIXTURE ONLY')
            env = dict(os.environ, APP_DIR=str(app), OUT_DIR=tmp, VERSION='1.0.0')
            result = subprocess.run(['bash', str(ROOT / 'installers/linux/build-native-package.sh')], env=env, capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            deb = str(Path(tmp) / 'Arkanum-1.0.0-linux-x64.deb')
            version = subprocess.check_output(['dpkg-deb', '-f', deb, 'Version'], text=True).strip()
            self.assertEqual('1:1.0.0', version)
            self.assertEqual(0, subprocess.run(['dpkg', '--compare-versions', version, 'gt', '2026.1.2']).returncode)
            names = subprocess.check_output(['dpkg-deb', '-c', deb], text=True)
            self.assertIn('./usr/lib/arkanum/app/Arkanum.Web.dll', names)
            self.assertNotIn('./opt/arkanum/Arkanum.Web.dll', names)
            self.assertNotIn('__pycache__', names)
            control = Path(tmp) / 'control'
            subprocess.run(['dpkg-deb','-e',deb,str(control)], check=True)
            rejected = subprocess.run(['bash',str(control/'preinst'),'upgrade','2026.1.2'], capture_output=True, text=True)
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn('legacy native DEB layout', rejected.stderr)
            self.assertEqual(0, subprocess.run(['bash',str(control/'preinst'),'install'], capture_output=True).returncode)
            payload = Path(tmp) / 'payload'
            subprocess.run(['dpkg-deb','-x',deb,str(payload)], check=True)
            manifest_path = payload/'usr/lib/arkanum/package-manifest.json'
            self.assertTrue(manifest_path.is_file(), 'native payload must carry the product version into CLI/GUI')
            manifest = json.loads(manifest_path.read_text())
            self.assertEqual('1.0.0', manifest['version'])
            self.assertEqual('full', manifest['packageKind'])
            self.assertTrue((payload/'usr/lib/arkanum/installers/sql/arkanum-schema.sql').is_file())

    def test_package_manifest_rejects_unknown_versions(self):
        spec = importlib.util.spec_from_file_location('release', ROOT / 'scripts/create-release-packages.py')
        assert spec is not None and spec.loader is not None
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as tmp:
            for version in ['test', '2027.1.0', '../1.0.0']:
                with self.subTest(version=version), self.assertRaises(ValueError):
                    module.write_manifest(Path(tmp), version, 'update', 'linux-x64', None)

    def test_native_constraints_fail_closed(self):
        helper = ROOT / 'installers/linux/arkanum_version.py'
        for value in ('1.0.0-rc.1', '1.0.0+local', '2026.1.2', '../bad'):
            with self.subTest(value=value):
                result = subprocess.run(['python3', str(helper), 'deb', value], capture_output=True)
                self.assertNotEqual(0, result.returncode)
        # MSI is an incomplete staging template, NOT a supported updater. It
        # must not feed numerically-lower 1.0.0 into an old MSI MajorUpgrade.
        text = (ROOT / 'installers/windows/Build-ArkanumMsi.ps1').read_text()
        self.assertIn('MSI remains unsupported for installation/upgrade', text)
        self.assertNotIn('wix.exe build', text)
        native = (ROOT / 'installers/linux/build-native-package.sh').read_text()
        self.assertIn('legacy native DEB layout', native)
        self.assertIn('DEBIAN/preinst', native)

    def test_upgrade_preservation_contracts(self):
        linux = (ROOT / 'installers/linux/install-arkanum.sh').read_text()
        windows = (ROOT / 'installers/windows/Install-Arkanum.ps1').read_text()
        self.assertIn('Preserving existing systemd environment and service unit', linux)
        self.assertIn('Preserving existing IIS configuration and bindings', windows)
        # Schema/sentinel validation must not depend on the product number.
        self.assertIn('--database-setup preflight', linux)
        self.assertIn('ARKANUM_MASTER_KEY_FILE="${EXISTING_MASTER_KEY_FILE}"', linux)

    def test_minimum_version_policy_on_both_platforms(self):
        result = subprocess.run(['python3', str(ROOT / 'installers/linux/arkanum_version.py'), 'check',
                                 '1.0.0', '1.2.0', 'upgrade', '1.1.0'], capture_output=True)
        self.assertNotEqual(0, result.returncode)
        linux = (ROOT / 'installers/linux/install-arkanum.sh').read_text()
        self.assertIn('minimumInstalledVersion', linux)
        if shutil.which('pwsh'):
            script = f". '{ROOT}/installers/windows/Arkanum-Version.ps1'; try {{ Assert-ArkanumVersionTransition -InstalledVersion 1.0.0 -PackageVersion 1.2.0 -Action upgrade -MinimumInstalledVersion 1.1.0; throw 'floor ignored' }} catch {{ if($_ -like '*floor ignored*'){{throw}} }}; Assert-ArkanumVersionTransition -InstalledVersion 2026.1.2 -PackageVersion 1.0.0 -Action upgrade -MinimumInstalledVersion 2026.1.2"
            result = subprocess.run(['pwsh','-NoProfile','-Command',script], capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stdout+result.stderr)

    def test_windows_archive_upgrade_preserves_files_with_native_boundaries_mocked(self):
        if not shutil.which('pwsh'):
            self.skipTest('PowerShell unavailable')
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            app = root / 'package/app'; app.mkdir(parents=True)
            live = root / 'live'; live.mkdir()
            data = root / 'data'
            config = data / 'config'; config.mkdir(parents=True)
            secrets = data / 'secrets'; secrets.mkdir()
            (app / 'Arkanum.Web.dll').write_text('TEST NEW APP')
            (app / 'Arkanum.Web.runtimeconfig.json').write_text('{}')
            (app / 'appsettings.json').write_text('{"default":"new"}')
            (app / 'web.config').write_text('<new/>')
            (root / 'package/package-manifest.json').write_text(json.dumps({'version':'1.0.0','packageKind':'update'}))
            (live / 'Arkanum.Web.dll').write_text('TEST LEGACY APP')
            (live / 'appsettings.json').write_text('{"custom":"preserve"}')
            (live / 'web.config').write_text('<custom-bindings-and-environment/>')
            (config / 'install-state.json').write_text(json.dumps({'installedVersion':'2026.1.2'}))
            (config / 'arkanum.runtime.json').write_text('{"custom":"preserve"}')
            (secrets / 'master-key.txt').write_text('ark-recovery-v1.TEST-ONLY-not-real-material')
            (secrets / 'sql-connection.txt').write_text('Server=db;Database=Arkanum;User ID=runtime;Password=TEST-ONLY;Encrypt=True')
            before = {p:p.read_bytes() for p in [live/'appsettings.json', live/'web.config', config/'arkanum.runtime.json', secrets/'master-key.txt', secrets/'sql-connection.txt']}
            for kind in ('full','update'):
                (config / 'install-state.json').write_text(json.dumps({'installedVersion':'2026.1.2'}))
                (live / 'Arkanum.Web.dll').write_text('TEST LEGACY APP')
                (root / 'package/package-manifest.json').write_text(json.dumps({'version':'1.0.0','packageKind':kind}))
                script = f"""
$ErrorActionPreference='Stop'
. '{ROOT}/installers/windows/Arkanum-Prerequisites.ps1'
. '{ROOT}/installers/windows/Arkanum-Version.ps1'
$source=Get-Content '{ROOT}/installers/windows/Install-Arkanum.ps1' -Raw
$source=$source.Replace('. (Join-Path $PSScriptRoot ''Arkanum-Prerequisites.ps1'')','')
$source=$source.Replace('. (Join-Path $PSScriptRoot ''Arkanum-Version.ps1'')','')
$source=$source.Replace("`nAssert-ArkanumWindowsHost`n","`n")
# Test-only removal of Windows ACL APIs (not supported by Linux .NET).
$start=$source.IndexOf('$aclTargets ='); $end=$source.IndexOf('$connectionStringPath =', $start)
$source=$source.Remove($start,$end-$start)
$source=$source.Replace('Protect-ArkanumSecretFile -Path $keyPath','')
$source=$source.Replace('Protect-ArkanumSecretFile -Path $connectionStringPath','')
function Test-Path {{ param($LiteralPath,$Path) $p=if($LiteralPath){{$LiteralPath}}else{{$Path}}; if($p -like 'IIS:*'){{return $true}}; Microsoft.PowerShell.Management\\Test-Path -LiteralPath $p }}
function Get-Item {{ param($Path,$ErrorAction) if($Path -like 'Cert:*'){{return [pscustomobject]@{{Thumbprint='TEST'}}}}; Microsoft.PowerShell.Management\\Get-Item $Path }}
function Get-ArkanumPrerequisitePlan {{ [pscustomobject]@{{MissingFeatures=@(); NeedsHosting=$false; Dotnet='dotnet'}} }}
function Invoke-ArkanumPrerequisites {{}}
function Import-Module {{}}
function Stop-WebAppPool {{}}
function Restart-WebAppPool {{}}
function Set-ItemProperty {{ param($Path,$Name,$Value) if($Name -ne 'managedRuntimeVersion'){{throw 'IIS mutation forbidden'}} }}
function Invoke-ArkanumDatabaseCommand {{ param($DotnetPath,$AppDll,$Mode,$RuntimeConnection,$MasterKeyFile) if($Mode -ne 'preflight' -or $MasterKeyFile -notlike '*master-key.txt'){{throw 'wrong DB operation'}} }}
function Invoke-WebRequest {{ [pscustomobject]@{{Content='{{"status":"Alive"}}'}} }}
& ([scriptblock]::Create($source)) -PackagePath '{root}/package' -InstallPath '{live}' -ProgramDataPath '{data}' -HttpsCertificateThumbprint TEST
"""
                result = subprocess.run(['pwsh','-NoProfile','-Command',script], capture_output=True, text=True)
                self.assertEqual(0, result.returncode, result.stdout+result.stderr)
                self.assertEqual(before, {p:p.read_bytes() for p in before})
                state = json.loads((config/'install-state.json').read_text(encoding='utf-8-sig'))
                self.assertEqual('1.0.0', state['installedVersion'])
                self.assertEqual('TEST LEGACY APP', (Path(state['backupPath'])/'app/Arkanum.Web.dll').read_text())
            for version in (None, '', '1.0.0\n', '2027.1.0'):
                manifest = {'packageKind':'update'}
                if version is not None:
                    manifest['version'] = version
                (root/'package/package-manifest.json').write_text(json.dumps(manifest))
                result = subprocess.run(['pwsh','-NoProfile','-Command',script], capture_output=True, text=True)
                self.assertNotEqual(0, result.returncode, 'malformed Windows manifest was accepted: '+repr(version))

if __name__ == '__main__':
    unittest.main()
