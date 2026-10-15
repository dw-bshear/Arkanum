"""Offline packaging tests. Synthetic installer bytes are TEST FIXTURES ONLY."""
import argparse

import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import tarfile

ROOT = Path(__file__).resolve().parents[1]

def load(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), ROOT / 'scripts' / (name + '.py'))
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

class PrerequisiteTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.cache = self.root / 'cache'
        self.cache.mkdir()
        # Never shipped: fixture bytes exercise hash checking and archive layout.
        self.payload = b'TEST FIXTURE ONLY - not an executable'
        self.manifest = dict(fileName='dotnet-hosting-10.0.13-win.exe', version='10.0.13',
                             sha512=hashlib.sha512(self.payload).hexdigest(),
                             downloadUrl='https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.13/dotnet-hosting-10.0.13-win.exe')
        (self.root / 'prerequisites').mkdir()
        self.pin = self.root / 'prerequisites/hosting-bundle.json'
        self.pin.write_text(json.dumps(self.manifest))
        (self.cache / 'hosting-bundle.json').write_text(json.dumps(self.manifest))
        (self.cache / self.manifest['fileName']).write_bytes(self.payload)

    def test_both_windows_packages_include_only_validated_prerequisites(self):
        package = load('create-release-packages')
        for directory in ['installers', 'docs', 'win', 'linux']:
            (self.root / directory).mkdir()
            (self.root / directory / 'fixture.txt').write_text('test only')
        (self.cache / 'private.key').write_text('must not ship')
        args = argparse.Namespace(root=self.root, out_dir=self.root / 'out', windows_publish=self.root / 'win',
                                  linux_publish=self.root / 'linux', version='test', minimum_installed_version=None,
                                  windows_prerequisites=self.cache)
        assets = package.build_assets(args)
        for path in assets:
            if path.name.endswith('.zip'):
                with zipfile.ZipFile(path) as archive:
                    names = {n for n in archive.namelist() if n.startswith('prerequisites/')}
                    self.assertEqual(names, {'prerequisites/hosting-bundle.json', 'prerequisites/' + self.manifest['fileName']})
                    self.assertEqual(archive.read('prerequisites/' + self.manifest['fileName']), self.payload)
            elif path.name.endswith('.tar.gz'):
                with tarfile.open(path) as archive:
                    self.assertFalse(any(n.startswith('prerequisites') for n in archive.getnames()))

    def test_missing_tampered_and_unpinned_files_fail(self):
        fetch = load('fetch-windows-prerequisites')
        fetch.validate_cache(self.cache, self.pin)
        binary = self.cache / self.manifest['fileName']
        binary.write_bytes(b'tampered')
        with self.assertRaisesRegex(ValueError, 'SHA512'):
            fetch.validate_cache(self.cache, self.pin)
        binary.unlink()
        with self.assertRaises(FileNotFoundError):
            fetch.validate_cache(self.cache, self.pin)
        binary.write_bytes(self.payload)
        changed = dict(self.manifest, sha512='a' * 128)
        (self.cache / 'hosting-bundle.json').write_text(json.dumps(changed))
        with self.assertRaisesRegex(ValueError, 'pinned'):
            fetch.validate_cache(self.cache, self.pin)

    def test_copy_omits_python_caches_and_tar_members_are_unique(self):
        package = load('create-release-packages')
        source = self.root / 'source'
        (source / '__pycache__').mkdir(parents=True)
        (source / '__pycache__/installer.pyc').write_bytes(b'cache')
        (source / 'installer.py').write_text('print("fixture")')
        stage = self.root / 'stage'
        package.copy_tree(source, stage / 'installers')
        self.assertFalse((stage / 'installers/__pycache__').exists())
        archive_path = self.root / 'package.tar.gz'
        package.make_tar(stage, archive_path)
        with tarfile.open(archive_path) as archive:
            names = archive.getnames()
        self.assertEqual(len(names), len(set(names)), 'TAR must not recursively repeat members')

    def test_manifest_rejects_traversal_unofficial_urls_and_prereleases(self):
        fetch = load('fetch-windows-prerequisites')
        for key, value in [('fileName', '../evil.exe'), ('fileName', '..\\evil.exe'),
                           ('version', '10.0.14-preview.1'), ('sha512', 'a'),
                           ('downloadUrl', 'http://builds.dotnet.microsoft.com/evil.exe'),
                           ('downloadUrl', self.manifest['downloadUrl'] + '?x=1'),
                           ('downloadUrl', self.manifest['downloadUrl'].replace('builds.dotnet.microsoft.com', 'builds.dotnet.microsoft.com.evil.test'))]:
            with self.subTest(key=key, value=value):
                with self.assertRaises(ValueError):
                    fetch.validate_manifest(dict(self.manifest, **{key: value}))

    def test_symlinked_binary_rejected(self):
        fetch = load('fetch-windows-prerequisites')
        binary = self.cache / self.manifest['fileName']
        binary.unlink()
        target = self.root / 'outside.exe'
        target.write_bytes(self.payload)
        binary.symlink_to(target)
        with self.assertRaises(ValueError):
            fetch.validate_cache(self.cache, self.pin)

    def test_staging_missing_default_cache_fails_closed(self):
        package = load('create-release-packages')
        with self.assertRaises(FileNotFoundError):
            package.stage_package(self.root, self.root / 'publish', self.root / 'stage', 'test',
                                  'update', 'windows-x64', None)

    def test_official_redirect_policy_rejects_other_hosts(self):
        fetch = load('fetch-windows-prerequisites')
        for url in ['http://builds.dotnet.microsoft.com/file', 'https://evil.example/file',
                    'https://builds.dotnet.microsoft.com@evil.example/file',
                    'https://builds.dotnet.microsoft.com:443/file']:
            with self.subTest(url=url):
                with self.assertRaises(ValueError):
                    fetch.OfficialRedirectHandler().redirect_request(None, None, 302, 'redirect', {}, url)

    def metadata(self):
        return {'releases': [{'release-version': self.manifest['version'], 'aspnetcore-runtime': {
            'files': [{'name': 'dotnet-hosting-win.exe', 'url': self.manifest['downloadUrl'], 'hash': self.manifest['sha512']}]}}]}

    def test_fetch_checks_metadata_then_downloads_and_validates(self):
        fetch = load('fetch-windows-prerequisites')
        destination = self.root / 'download'
        def response(url):
            return io.BytesIO(json.dumps(self.metadata()).encode() if url == fetch.METADATA_URL else self.payload)
        with patch.object(fetch, 'open_official', side_effect=response) as opener:
            fetch.fetch_prerequisites(self.pin, destination)
        self.assertEqual(opener.call_args_list[0].args[0], fetch.METADATA_URL)
        self.assertEqual((destination / self.manifest['fileName']).read_bytes(), self.payload)
        fetch.validate_cache(destination, self.pin)

    def test_metadata_disagreement_prevents_download(self):
        fetch = load('fetch-windows-prerequisites')
        metadata = self.metadata()
        metadata['releases'][0]['aspnetcore-runtime']['files'][0]['hash'] = 'a' * 128
        with patch.object(fetch, 'open_official', return_value=io.BytesIO(json.dumps(metadata).encode())) as opener:
            with self.assertRaisesRegex(ValueError, 'metadata'):
                fetch.fetch_prerequisites(self.pin, self.root / 'download')
        self.assertEqual(opener.call_count, 1)

    def test_bad_download_is_not_promoted(self):
        fetch = load('fetch-windows-prerequisites')
        destination = self.root / 'download'
        def response(url):
            return io.BytesIO(json.dumps(self.metadata()).encode() if url == fetch.METADATA_URL else b'bad download')
        with patch.object(fetch, 'open_official', side_effect=response):
            with self.assertRaisesRegex(ValueError, 'SHA512'):
                fetch.fetch_prerequisites(self.pin, destination)
        self.assertFalse((destination / self.manifest['fileName']).exists())
        self.assertFalse((destination / 'hosting-bundle.json').exists())

if __name__ == '__main__':
    unittest.main()
