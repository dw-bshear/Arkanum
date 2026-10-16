"""Fail-closed evidence packaging contract; synthetic fixture data only."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

SPEC = importlib.util.spec_from_file_location('security_evidence', Path(__file__).parents[1] / 'scripts/security_evidence.py')
assert SPEC is not None and SPEC.loader is not None


class EvidencePackagerTests(unittest.TestCase):
    def setUp(self):
        self.assertTrue(SPEC.origin and Path(SPEC.origin).is_file(), 'first-class evidence packager is missing')
        self.mod = importlib.util.module_from_spec(SPEC)
        SPEC.loader.exec_module(self.mod)
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'bundle'
        self.root.mkdir()
        for name in self.mod.REQUIRED:
            p = self.root / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text('Synthetic evidence fixture\n')
        self.manifest: dict = dict(release=dict(version='2026.1.2', tag='v2026.1.2', sourceCommit=self.mod.SOURCE_COMMIT), evidenceToolingCommit='a' * 40, generatedAtUtc='2026-10-16T18:00:00Z', assessmentType='developer-prepared; not independent government authorization')
        self.executions = [dict(id=n, command='synthetic-fixture --test', version='fixture 1', startedAtUtc='2026-10-16T17:00:00Z', endedAtUtc='2026-10-16T17:01:00Z', exitStatus=0, outputPath='README.md', scope='synthetic test only', status='passed') for n in self.mod.MANDATORY_CHECKS]
        self.save()

    def save(self):
        (self.root / 'manifest.json').write_text(json.dumps(self.manifest))
        (self.root / 'evidence/execution-manifest.json').write_text(json.dumps({'executions': self.executions}))

    def test_roundtrip_hashes_and_portable_checksum(self):
        archive = self.mod.package(self.root, Path(self.temp.name) / 'out')
        self.mod.verify_zip(archive)
        checksum = Path(str(archive) + '.sha256').read_text()
        self.assertEqual(checksum.split()[1], archive.name)
        with zipfile.ZipFile(archive) as z:
            self.assertEqual(set(z.namelist()), set(self.mod.REQUIRED) | {'SHA256SUMS.txt'})

    def test_missing_required_rejected(self):
        (self.root / 'sbom/windows-app.cdx.json').unlink()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_source_identity_cannot_follow_later_main(self):
        self.manifest['release']['sourceCommit'] = 'b' * 40
        self.save()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_uncommitted_tooling_rejected(self):
        self.manifest['evidenceToolingCommit'] = 'uncommitted'
        self.save()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_invalid_or_missing_execution_status_rejected(self):
        for status in ['skipped', '', 'clean']:
            self.executions[0]['status'] = status
            self.save()
            with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_pass_cannot_hide_failure(self):
        self.executions[0]['exitStatus'] = 2
        self.save()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_missing_output_and_missing_check_rejected(self):
        self.executions[0]['outputPath'] = 'missing.json'
        self.save()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)
        self.executions = self.executions[1:]
        self.save()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_utc_order_and_manifest_type(self):
        self.executions[0]['endedAtUtc'] = '2020-01-01T00:00:00Z'
        self.save()
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_symlinks_and_sensitive_names_rejected(self):
        p = self.root / 'linked'
        p.symlink_to(self.root / 'README.md')
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)
        p.unlink()
        (self.root / '.env').write_text('synthetic')
        with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_host_paths_private_ips_and_secret_material_rejected(self):
        for value in ['/home/example/private', '192.168.4.5', 'Password=synthetic-secret;', '-----BEGIN PRIVATE KEY-----', 'ark-recovery-v1.syntheticfixture']:
            (self.root / 'README.md').write_text(value)
            with self.assertRaises(ValueError): self.mod.validate_tree(self.root)

    def test_hash_tampering_rejected(self):
        archive = self.mod.package(self.root, Path(self.temp.name) / 'out')
        with zipfile.ZipFile(archive) as z: data = {n: z.read(n) for n in z.namelist()}
        data['README.md'] = b'tampered'
        with zipfile.ZipFile(archive, 'w') as z:
            for n, b in data.items(): z.writestr(n, b)
        with self.assertRaises(ValueError): self.mod.verify_zip(archive)

    def test_json_secrets_sensitive_variants_and_control_paths(self):
        for value in ['{"Password": "synthetic-secret"}', '{"clientSecret":"synthetic-secret"}']:
            (self.root / 'README.md').write_text(value)
            with self.assertRaises(ValueError): self.mod.validate_tree(self.root)
        for name in ['.env.production', 'id_rsa', 'bad\nname.txt']:
            with self.assertRaises(ValueError): self.mod.safe_path(name)

    def test_root_symlink_rejected(self):
        linked = Path(self.temp.name) / 'root-link'
        linked.symlink_to(self.root, target_is_directory=True)
        with self.assertRaises(ValueError): self.mod.validate_tree(linked)

    def test_traversal_and_duplicate_zip_members_rejected(self):
        archive = Path(self.temp.name) / 'bad.zip'
        with zipfile.ZipFile(archive, 'w') as z: z.writestr('../escape', 'fixture')
        with self.assertRaises(ValueError): self.mod.verify_zip(archive)
        with zipfile.ZipFile(archive, 'w') as z:
            z.writestr('README.md', 'fixture')
            with self.assertWarns(UserWarning): z.writestr('README.md', 'duplicate')
        with self.assertRaises(ValueError): self.mod.verify_zip(archive)


if __name__ == '__main__':
    unittest.main()
