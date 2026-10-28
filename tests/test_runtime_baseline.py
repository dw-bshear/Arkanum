"""Release runtime contract; runnable without a .NET SDK."""
import json
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]


class RuntimeBaselineTests(unittest.TestCase):
    def test_operator_guide_explains_manual_boundary_and_restart_consent(self):
        path = ROOT / 'docs/windows-server-2022-setup.md'
        self.assertTrue(path.is_file(), 'Server 2022 operator guide is required')
        guide = path.read_text()
        for term in ('Windows PowerShell 5.1', 'PowerShell 7', 'Server Core', 'WindowsFeatureSource', 'InstallHostingBundle', 'EnableIisFeatures', 'AllowIisServiceInterruption', 'sqlcmd', 'SHA-512', 'Authenticode', '3010', 'SQL Server', 'certificate', 'not yet'):
            with self.subTest(term=term):
                self.assertIn(term, guide)

    def test_next_source_version_and_linux_runtime_dependency(self):
        props = ET.parse(ROOT / 'Directory.Build.props')
        self.assertEqual('1.1.1', props.findtext('.//Version'))
        native = (ROOT / 'installers/linux/build-native-package.sh').read_text()
        self.assertIn('aspnetcore-runtime-10.0', native)

    def test_dotnet10_crypto_apis_and_patched_pkcs_dependency(self):
        source = (ROOT / 'Arkanum.Web/Services/VaultExportService.cs').read_text()
        self.assertNotIn('new Rfc2898DeriveBytes(', source)
        project = ET.parse(ROOT / 'Arkanum.Web/Arkanum.Web.csproj')
        pkcs = [p for p in project.findall('.//PackageReference') if p.get('Include') == 'System.Security.Cryptography.Pkcs']
        self.assertEqual(1, len(pkcs))
        self.assertEqual('10.0.13', pkcs[0].get('Version'))

    def test_both_projects_target_dotnet10(self):
        for name in ('Arkanum.Web', 'Arkanum.Tests'):
            with self.subTest(project=name):
                project = ET.parse(ROOT / name / (name + '.csproj'))
                self.assertEqual('net10.0', project.findtext('.//TargetFramework'))

    def test_microsoft_runtime_dependencies_match_lts_major(self):
        for name in ('Arkanum.Web', 'Arkanum.Tests'):
            project = ET.parse(ROOT / name / (name + '.csproj'))
            for package in project.findall('.//PackageReference'):
                if package.get('Include', '').startswith(('Microsoft.EntityFrameworkCore.', 'Microsoft.AspNetCore.', 'System.DirectoryServices.')):
                    with self.subTest(package=package.get('Include')):
                        self.assertTrue(package.get('Version', '').startswith('10.0.'))

    def test_sdk_is_pinned_and_published_rids_have_lock_graphs(self):
        self.assertTrue((ROOT / 'global.json').is_file(), 'SDK pin is required')
        sdk = json.loads((ROOT / 'global.json').read_text())['sdk']
        self.assertTrue(sdk['version'].startswith('10.0.'))
        self.assertFalse(sdk['allowPrerelease'])
        project = ET.parse(ROOT / 'Arkanum.Web/Arkanum.Web.csproj')
        self.assertEqual({'win-x64', 'linux-x64'}, set(project.findtext('.//RuntimeIdentifiers', '').split(';')))
        locks = json.loads((ROOT / 'Arkanum.Web/packages.lock.json').read_text())['dependencies']
        self.assertIn('net10.0/win-x64', locks)
        self.assertIn('net10.0/linux-x64', locks)


if __name__ == '__main__':
    unittest.main()
