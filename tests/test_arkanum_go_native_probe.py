"""Linux source contracts; set ARKANUM_GO_NATIVE_PROBE on Windows for real acceptance."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PROBE = ROOT / 'Arkanum.Go.NativeProbe'
REQUIRED = {'windows_identity', 'canonical_sid_round_trip', 'cng_signature_valid',
            'cng_signature_tamper_rejected', 'aes_gcm_round_trip', 'aes_gcm_tamper_rejected',
            'pipe_same_caller_allowed', 'pipe_unassigned_denied', 'pipe_forged_sid_denied',
            'pipe_wrong_server_pid_rejected', 'pipe_first_instance_collision_rejected'}


class NativeProbeContractTests(unittest.TestCase):
    def source(self):
        path = PROBE / 'Program.cs'
        self.assertTrue(path.exists(), 'native primitive executable is missing')
        return path.read_text()

    def test_project_is_standalone_windows_cli_with_independent_version(self):
        path = PROBE / 'Arkanum.Go.NativeProbe.csproj'
        self.assertTrue(path.exists(), 'probe project is missing')
        root = ET.parse(path).getroot()
        for key, value in {'TargetFramework': 'net10.0-windows', 'OutputType': 'Exe',
                           'Version': '1.0.0-beta.1', 'AssemblyVersion': '1.0.0.0',
                           'FileVersion': '1.0.0.0', 'InformationalVersion': '1.0.0-beta.1'}.items():
            self.assertEqual(root.findtext(f'.//{key}'), value)
        self.assertFalse(root.findall('.//PackageReference'))
        self.assertFalse(root.findall('.//ProjectReference'))

    def test_native_primitives_and_individual_evidence(self):
        source = self.source()
        for token in REQUIRED | {'WindowsIdentity.GetCurrent', 'SecurityIdentifier', 'RSACng',
                                 'CngKeyCreationOptions.None', 'AesGcm', 'CryptographicException'}:
            self.assertIn(token, source)

    def test_pipe_os_identity_and_local_security_boundary(self):
        source = self.source()
        for token in ['CreateNamedPipeW', 'FILE_FLAG_FIRST_PIPE_INSTANCE', 'PIPE_REJECT_REMOTE_CLIENTS',
                      'ConvertStringSecurityDescriptorToSecurityDescriptorW', 'D:P(A;;GA;;;',
                      'RunAsClient', 'GetNamedPipeServerProcessId', 'TokenImpersonationLevel.Impersonation']:
            self.assertIn(token, source)
        self.assertIn('authenticatedSid == allowedSid', source)
        self.assertLess(source.index('await ReadRequest'), source.index('server.RunAsClient'))
        self.assertLess(source.index('actualPid != expectedPid'), source.index('await client.WriteAsync'))

    def test_bounded_lifecycle_and_cli(self):
        source = self.source()
        for token in ['--self-test', '--identity', 'Path.IsPathFullyQualified', 'Kill(entireProcessTree: true)',
                      'ArgumentList', 'Environment.ProcessPath', 'WaitForExitAsync', 'CancelAfter',
                      'TimeSpan.FromSeconds(55)', 'Environment.Exit(124)']:
            self.assertIn(token, source)

    def test_crypto_setup_is_inside_individual_result_boundary(self):
        source = self.source()
        for name, call in [('cng_signature_valid', 'RsaProbe(false)'),
                           ('cng_signature_tamper_rejected', 'RsaProbe(true)'),
                           ('aes_gcm_round_trip', 'AesProbe(false)'),
                           ('aes_gcm_tamper_rejected', 'AesProbe(true)')]:
            self.assertIn(f'Check("{name}", () => {call});', source)

    def test_timeout_failure_can_still_write_evidence(self):
        source = self.source()
        self.assertIn('using var evidenceBudget', source)
        self.assertIn('evidenceBudget.Token', source)

    def test_first_instance_collision_is_not_a_max_instance_limit_test(self):
        source = self.source()
        self.assertIn('CreateServer(name, sid, maxInstances: 2)', source)
        self.assertIn('uint maxInstances = 1', source)
        self.assertIn('ex.NativeErrorCode == 5', source)

    def test_limitations_and_operator_instructions(self):
        path = PROBE / 'README.md'
        self.assertTrue(path.exists(), 'native acceptance instructions are missing')
        text = path.read_text().lower()
        for token in ['same-user', 'winrm', 'cross-user', 'ad mapping', 'service sid',
                      'persisted key', 'logged-out', 'sleep', 'reboot', '--self-test', '--identity',
                      'win-x64', 'synthetic', 'process identity', 'privilege']:
            self.assertIn(token, text)


@unittest.skipUnless(os.name == 'nt' and os.environ.get('ARKANUM_GO_NATIVE_PROBE'),
                     'requires real Windows executable; Linux contracts are not native acceptance')
class NativeProbeWindowsTests(unittest.TestCase):
    def test_real_native_self_test(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'native.json'
            run = subprocess.run([os.environ['ARKANUM_GO_NATIVE_PROBE'], '--self-test', str(output)],
                                 timeout=60, capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            report = json.loads(output.read_text(encoding='utf-8'))
            self.assertTrue(report['success'], report)
            self.assertEqual({x['name'] for x in report['results']}, REQUIRED)
            self.assertTrue(all(x['passed'] for x in report['results']))
            self.assertIn('sid', report['processIdentity'])
            self.assertIn('elevated', report['processIdentity'])
            self.assertTrue(report['limitations'])


if __name__ == '__main__':
    unittest.main()
