"""Archive -> shell integration with explicit test-only OS/SQL boundaries.

Runs actual packaged installer logic, rsync, backups, config and state files.
Only the disposable script's root check and external host commands are replaced.
This is NOT native systemd or live SQL preservation acceptance.
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
import unittest

ROOT = Path(__file__).resolve().parents[1]

class Liveness(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200); self.end_headers()
        self.wfile.write(getattr(self.server, 'health_body', b'{"status":"Alive"}'))
    def log_message(self, format, *args):
        pass

class InstallerIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.server = HTTPServer(('127.0.0.1', 0), Liveness)
        thread = threading.Thread(target=self.server.serve_forever, daemon=True); thread.start()
        self.addCleanup(self.server.server_close); self.addCleanup(self.server.shutdown)
        self.bin = self.root / 'bin'; self.bin.mkdir()
        self.command('chown', '#!/bin/sh\nexit 0\n')
        self.command('id', '#!/bin/sh\nexit 0\n')
        self.command('systemctl', '#!/bin/sh\nprintf "%s\\n" "$*" >> "$TEST_HOST_CALLS"\n')
        self.command('install', '''#!/usr/bin/env python3
import subprocess, sys
args=[]; it=iter(sys.argv[1:])
for value in it:
    if value in ('-o','-g'): next(it)
    else: args.append(value)
sys.exit(subprocess.call(['/usr/bin/install', *args]))
''')
        self.command('dotnet', '''#!/usr/bin/env python3
import os, pathlib, sys
assert sys.argv[2:] == ['--database-setup', 'preflight']
assert os.environ['ARKANUM_RUNTIME_CONNECTION'].strip() == 'TEST SQL CONNECTION ONLY'
assert pathlib.Path(os.environ['ARKANUM_MASTER_KEY_FILE']).read_text() == 'TEST RECOVERY MATERIAL ONLY\\n'
assert not os.environ.get('ARKANUM_MASTER_KEY')
# Explicit test double: no real SQL is queried here.
sys.exit(int(os.environ.get('TEST_SQL_FAILURE','0')))
''')
        self.env = dict(os.environ, PATH=str(self.bin)+os.pathsep+os.environ['PATH'],
                        APP_URLS=f'http://127.0.0.1:{self.server.server_port}', SERVICE_USER='test-only',
                        TEST_HOST_CALLS=str(self.root / 'host-calls'))
        # No inherited secrets are used; all connection/key values below are fixtures.
        for name in ('ARKANUM_SQL_CONNECTION','ARKANUM_MASTER_KEY','ARKANUM_RESTORE_KEY'):
            self.env.pop(name, None)
        for name, folder in [('INSTALL_DIR','live'), ('CONFIG_DIR','config'), ('DATA_DIR','data'), ('LOG_DIR','logs'), ('BACKUP_ROOT','backups')]:
            path = self.root / folder; path.mkdir(); self.env[name] = str(path)
        self.service = self.root / 'arkanum.service'
        self.service.write_text('[Service]\nExecStart=custom-old-command --urls https://custom:5443\nUser=custom\n')
        self.env['SERVICE_FILE'] = str(self.service)
        self.config = Path(self.env['CONFIG_DIR']); self.live = Path(self.env['INSTALL_DIR'])
        (self.live / 'Arkanum.Web.dll').write_bytes(b'TEST LEGACY APP')
        (self.live / 'appsettings.json').write_text('{"custom":"preserve"}')
        (self.config / 'install-state.json').write_text(json.dumps({'installedVersion':'2026.1.2'}))
        (self.config / 'master-key').write_text('TEST RECOVERY MATERIAL ONLY\n')
        (self.config / 'sql-connection').write_text('TEST SQL CONNECTION ONLY\n\n')
        (self.config / 'arkanum.env').write_text('CUSTOM_SETTING=preserve\n')
        (Path(self.env['DATA_DIR']) / 'encrypted-database.fixture').write_bytes(b'TEST ENCRYPTED DATA ONLY')

    def command(self, name, text):
        path = self.bin / name; path.write_text(text); path.chmod(0o755)

    def package(self, kind, version='1.0.0', minimum=None):
        spec = importlib.util.spec_from_file_location('packager', ROOT / 'scripts/create-release-packages.py')
        assert spec and spec.loader
        packager = importlib.util.module_from_spec(spec); spec.loader.exec_module(packager)
        published = self.root / 'published'; published.mkdir(exist_ok=True)
        (published / 'Arkanum.Web.dll').write_bytes(b'TEST NEW APP')
        (published / 'appsettings.json').write_text('{"defaults":"do not overwrite"}')
        stage = self.root / ('stage-' + kind)
        packager.stage_package(ROOT, published, stage, version, kind, 'linux-x64', minimum)
        archive = self.root / (kind + '.tar.gz'); packager.make_tar(stage, archive)
        extracted = self.root / ('extracted-' + kind); extracted.mkdir()
        shutil.unpack_archive(archive, extracted)
        installer = extracted / 'installers/linux/install-arkanum.sh'
        # Explicit unprivileged harness substitution, never modifies product source.
        source = installer.read_text()
        gate = 'if [ "${EUID}" -ne 0 ]; then\n  fail "Run as root or with sudo."\nfi'
        self.assertIn(gate, source)
        installer.write_text(source.replace(gate, '# TEST ONLY: host privilege check replaced'))
        return installer

    def run_installer(self, installer, **extra):
        return subprocess.run(['bash', str(installer)], env=dict(self.env, **extra), capture_output=True, text=True, timeout=30)

    def snapshot(self):
        paths = [self.config / n for n in ('master-key','sql-connection','arkanum.env')]
        paths += [self.service, self.live / 'appsettings.json', Path(self.env['DATA_DIR']) / 'encrypted-database.fixture']
        return {p:p.read_bytes() for p in paths}

    def test_full_and_update_transition_preserve_state_backup_and_repair(self):
        for kind in ('full','update'):
            with self.subTest(kind=kind):
                # Reset only the isolated fixture; no shared host/application state.
                (self.config / 'install-state.json').write_text(json.dumps({'installedVersion':'2026.1.2'}))
                (self.live / 'Arkanum.Web.dll').write_bytes(b'TEST LEGACY APP')
                installer = self.package(kind)
                before = self.snapshot()
                result = self.run_installer(installer)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(before, self.snapshot())
                state = json.loads((self.config / 'install-state.json').read_text())
                self.assertEqual('1.0.0', state['installedVersion'])
                backup = Path(state['backupPath'])
                self.assertEqual(b'TEST LEGACY APP', (backup / 'app/Arkanum.Web.dll').read_bytes())
                self.assertEqual(before[self.service], (backup / 'arkanum.service').read_bytes())
                self.assertFalse((backup / 'config/master-key').exists())
                report = (Path(self.env['LOG_DIR']) / 'upgrade-report-1.0.0.txt').read_text()
                self.assertIn('Verified by database sentinel preflight', report)
                self.assertIn('Post-install health status code: 0', report)
                self.assertNotIn('TEST RECOVERY MATERIAL', report)
                self.assertEqual(b'TEST NEW APP', (self.live / 'Arkanum.Web.dll').read_bytes())
                # Prove backup bytes restore application files without touching data.
                shutil.copy2(backup / 'app/Arkanum.Web.dll', self.live / 'Arkanum.Web.dll')
                self.assertEqual(b'TEST LEGACY APP', (self.live / 'Arkanum.Web.dll').read_bytes())
                result = self.run_installer(installer, ACTION='repair')
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertEqual(before, self.snapshot())

    def restore_preflight_double(self):
        self.command('dotnet', '''#!/usr/bin/env python3
import os, pathlib, stat, sys
assert sys.argv[2:] == ['--database-setup', 'preflight']
assert os.environ['ARKANUM_RUNTIME_CONNECTION'] == 'TEST SQL CONNECTION ONLY'
assert not os.environ.get('ARKANUM_MASTER_KEY')
key_file = pathlib.Path(os.environ['ARKANUM_MASTER_KEY_FILE'])
key = key_file.read_text()
assert key == os.environ['TEST_EXPECTED_RESTORE_KEY'] + '\\n'
if os.environ.get('TEST_RESTORE_SOURCE') == 'pasted':
    assert stat.S_IMODE(key_file.stat().st_mode) == 0o600
else:
    assert str(key_file) == os.environ['ARKANUM_RESTORE_KEY_FILE']
pathlib.Path(os.environ['TEST_SQL_CALLS']).write_text(str(key_file))
# Deliberately hostile provider diagnostics must not escape the installer.
print(key + os.environ['ARKANUM_RUNTIME_CONNECTION'])
print(key + os.environ['ARKANUM_RUNTIME_CONNECTION'], file=sys.stderr)
# Explicit SQL boundary double: 6 represents a missing or mismatched sentinel.
sys.exit(int(os.environ.get('TEST_SQL_FAILURE', '0')))
''')

    def restore_inputs(self, source):
        # Restore targets a fresh application location but must preserve any config/data.
        (self.config / 'install-state.json').unlink(missing_ok=True)
        (self.live / 'Arkanum.Web.dll').unlink(missing_ok=True)
        key = 'ark-recovery-v1.TEST-RESTORE-KEY-ONLY'
        inputs = dict(ACTION='restore', ARKANUM_SQL_CONNECTION='TEST SQL CONNECTION ONLY',
                      TEST_RESTORE_SOURCE=source, TEST_EXPECTED_RESTORE_KEY=key,
                      TEST_SQL_CALLS=str(self.root / 'sql-calls'))
        if source == 'file':
            key_file = self.root / 'provided-key'; key_file.write_text(key + '\n')
            inputs['ARKANUM_RESTORE_KEY_FILE'] = str(key_file)
        else:
            inputs['ARKANUM_RESTORE_KEY'] = key
        return inputs

    def test_restore_rejects_wrong_or_missing_sentinel_before_mutations_despite_alive(self):
        installer = self.package('full')
        self.restore_preflight_double()
        for source in ('file', 'pasted'):
            for sentinel in ('wrong', 'missing'):
                with self.subTest(source=source, sentinel=sentinel):
                    inputs = self.restore_inputs(source)
                    before = self.snapshot()
                    result = self.run_installer(installer, **inputs, TEST_SQL_FAILURE='6')
                    self.assertNotEqual(0, result.returncode, 'Public Alive cannot validate a Recovery Key')
                    self.assertIn('Database preflight failed', result.stderr)
                    self.assertEqual(before, self.snapshot())
                    self.assertFalse((self.config / 'install-state.json').exists())
                    self.assertFalse((self.live / 'Arkanum.Web.dll').exists())
                    self.assertFalse((self.root / 'host-calls').exists())
                    self.assertEqual([], list(Path(self.env['LOG_DIR']).iterdir()))
                    self.assertEqual([], list(Path(self.env['BACKUP_ROOT']).iterdir()))
                    checked_file = Path((self.root / 'sql-calls').read_text())
                    self.assertEqual(source == 'file', checked_file.exists())
                    for secret in (inputs['TEST_EXPECTED_RESTORE_KEY'], inputs['ARKANUM_SQL_CONNECTION']):
                        self.assertNotIn(secret, result.stdout + result.stderr)

    def test_restore_file_and_paste_use_supplied_key_for_sentinel_proof(self):
        installer = self.package('full')
        self.restore_preflight_double()
        for source in ('file', 'pasted'):
            with self.subTest(source=source):
                inputs = self.restore_inputs(source)
                result = self.run_installer(installer, **inputs)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                self.assertTrue((self.root / 'sql-calls').exists(), 'Restore skipped SQL sentinel preflight')
                checked_file = Path((self.root / 'sql-calls').read_text())
                self.assertEqual(source == 'file', checked_file.exists())
                self.assertEqual(inputs['TEST_EXPECTED_RESTORE_KEY'] + '\n', (self.config / 'master-key').read_text())
                report = (Path(self.env['LOG_DIR']) / 'restore-report-1.0.0.txt').read_text()
                self.assertIn('Recovery sentinel validation: Verified by database sentinel preflight', report)
                self.assertIn('Post-install health status code: 0', report)
                for secret in (inputs['TEST_EXPECTED_RESTORE_KEY'], inputs['ARKANUM_SQL_CONNECTION']):
                    self.assertNotIn(secret, result.stdout + result.stderr + report)

    def test_restore_liveness_failure_does_not_claim_recovery_key_mismatch(self):
        installer = self.package('full')
        self.restore_preflight_double()
        setattr(self.server, 'health_body', b'{"status":"Unavailable"}')
        result = self.run_installer(installer, **self.restore_inputs('pasted'))
        self.assertNotEqual(0, result.returncode)
        report = (Path(self.env['LOG_DIR']) / 'restore-report-1.0.0.txt').read_text()
        self.assertIn('Recovery sentinel validation: Verified by database sentinel preflight', report)
        self.assertIn('liveness validation failed', result.stderr)
        self.assertNotIn('does not match', result.stderr)

    def test_restore_missing_key_file_stops_before_mutations(self):
        installer = self.package('full')
        inputs = self.restore_inputs('file')
        Path(inputs['ARKANUM_RESTORE_KEY_FILE']).unlink()
        before = self.snapshot()
        result = self.run_installer(installer, **inputs)
        self.assertNotEqual(0, result.returncode)
        self.assertIn('does not exist or is empty', result.stderr)
        self.assertEqual(before, self.snapshot())
        self.assertFalse((self.root / 'host-calls').exists())
        self.assertFalse((self.live / 'Arkanum.Web.dll').exists())

    def test_restore_preflight_only_still_validates_supplied_key(self):
        installer = self.package('full')
        self.restore_preflight_double()
        inputs = self.restore_inputs('pasted')
        before = self.snapshot()
        result = self.run_installer(installer, **inputs, PREFLIGHT_ONLY='true', TEST_SQL_FAILURE='6')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Database preflight failed', result.stderr)
        self.assertEqual(before, self.snapshot())
        self.assertFalse((self.root / 'host-calls').exists())
        self.assertFalse(Path((self.root / 'sql-calls').read_text()).exists())

    def test_restore_rejects_provisioning_instead_of_mutating_restored_database(self):
        installer = self.package('full')
        inputs = self.restore_inputs('pasted')
        self.command('sqlcmd', '#!/bin/sh\nprintf provisioning >> "$TEST_HOST_CALLS"\n')
        result = self.run_installer(installer, **inputs, DATABASE_SETUP_MODE='create-new-database',
                                    SETUP_SQL_USERNAME='TEST_ADMIN', SETUP_SQL_PASSWORD='TEST_PASSWORD',
                                    RUNTIME_SQL_PASSWORD='TEST_PASSWORD')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Restore cannot provision', result.stderr)
        self.assertFalse((self.root / 'host-calls').exists())

    def test_sql_failure_stops_before_backup_replacement_or_service_stop(self):
        installer = self.package('update')
        before = self.snapshot()
        result = self.run_installer(installer, TEST_SQL_FAILURE='4')
        self.assertNotEqual(0, result.returncode)
        self.assertIn('Database preflight failed', result.stderr)
        self.assertEqual(before, self.snapshot())
        self.assertEqual(b'TEST LEGACY APP', (self.live / 'Arkanum.Web.dll').read_bytes())
        self.assertFalse((self.root / 'host-calls').exists())

    def test_backup_failure_stops_before_replacement_or_service_stop(self):
        installer = self.package('update')
        real_rsync = shutil.which('rsync')
        self.command('rsync', '#!/bin/sh\ncase "$*" in *before-*) exit 42;; esac\nexec ' + str(real_rsync) + ' "$@"\n')
        result = self.run_installer(installer)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual(b'TEST LEGACY APP', (self.live / 'Arkanum.Web.dll').read_bytes())
        self.assertFalse((self.root / 'host-calls').exists())

    def test_downgrade_unknown_and_minimum_reject_in_real_shell(self):
        installer = self.package('update', minimum='1.1.0', version='1.2.0')
        for version in ('unknown','2026.1.1','2.0.0','1.0.0','1.2.0\n'):
            with self.subTest(version=version):
                (self.config / 'install-state.json').write_text(json.dumps({'installedVersion':version}))
                result = self.run_installer(installer, PREFLIGHT_ONLY='true')
                self.assertNotEqual(0, result.returncode)
                self.assertTrue('Version transition rejected' in result.stderr or 'Invalid install state' in result.stderr, result.stderr)
                self.assertFalse((self.root / 'host-calls').exists())

    def test_manifest_version_is_validated_before_shell_newline_trimming(self):
        installer = self.package('update')
        manifest = installer.parents[2] / 'package-manifest.json'
        manifest.write_text(json.dumps({'version':'1.0.0\n','packageKind':'update'}))
        result = self.run_installer(installer, PREFLIGHT_ONLY='true')
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.root / 'host-calls').exists())

    def test_linux_verifier_accepts_liveness_without_claiming_database_health(self):
        result = subprocess.run(['bash', str(ROOT/'installers/linux/verify-arkanum.sh')],
                                env=dict(self.env, APP_URL=self.env['APP_URLS']), capture_output=True, text=True)
        self.assertEqual(0, result.returncode, result.stdout+result.stderr)
        self.assertIn('liveness only', result.stdout)

if __name__ == '__main__':
    unittest.main()
