#!/usr/bin/env python3
"""Fetch the pinned Microsoft Hosting Bundle; never ship test substitute bytes.

The checked-in prerequisites/hosting-bundle.json pins the release. Downloads are
verified against Microsoft's release metadata and SHA512 before atomic promotion.
Windows installation additionally verifies Authenticode (not available on Linux).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
METADATA_URL = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
OFFICIAL_HOSTS = {'builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com', 'dotnetcli.azureedge.net'}


def validate_official_url(url: str) -> None:
    parsed = urllib.parse.urlsplit(url)
    if (parsed.scheme != 'https' or parsed.netloc not in OFFICIAL_HOSTS
            or parsed.username or parsed.password or parsed.query or parsed.fragment):
        raise ValueError('Only official Microsoft HTTPS download URLs are allowed')


class OfficialRedirectHandler(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        validate_official_url(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def open_official(url: str):
    validate_official_url(url)
    response = urllib.request.build_opener(OfficialRedirectHandler()).open(url, timeout=120)
    validate_official_url(response.geturl())
    return response


def validate_manifest(manifest: dict) -> dict:
    if not isinstance(manifest, dict) or set(manifest) != {'fileName', 'version', 'sha512', 'downloadUrl'}:
        raise ValueError('Invalid Hosting Bundle manifest fields')
    if not all(isinstance(value, str) for value in manifest.values()):
        raise ValueError('Manifest values must be strings')
    version = manifest['version']
    if not re.fullmatch(r'10\.0\.[0-9]+', version):
        raise ValueError('Hosting Bundle must pin a stable .NET 10 release')
    filename = f'dotnet-hosting-{version}-win.exe'
    if manifest['fileName'] != filename:
        raise ValueError('Invalid Hosting Bundle fileName (paths are not allowed)')
    if not re.fullmatch(r'[0-9a-fA-F]{128}', manifest['sha512']):
        raise ValueError('Invalid SHA512 value')
    validate_official_url(manifest['downloadUrl'])
    if urllib.parse.urlsplit(manifest['downloadUrl']).path != f'/dotnet/aspnetcore/Runtime/{version}/{filename}':
        raise ValueError('Hosting Bundle URL does not match pinned release')
    return manifest


def read_manifest(path: Path) -> dict:
    if path.is_symlink():
        raise ValueError('Symlinked prerequisite manifest is not allowed')
    return validate_manifest(json.loads(path.read_text(encoding='utf-8')))


def sha512_file(path: Path) -> str:
    digest = hashlib.sha512()
    with path.open('rb') as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def validate_cache(directory: Path, pin: Path) -> tuple[Path, Path]:
    expected = read_manifest(pin)
    manifest_path = directory / 'hosting-bundle.json'
    if read_manifest(manifest_path) != expected:
        raise ValueError('Cached Hosting Bundle manifest differs from pinned manifest')
    binary = directory / expected['fileName']
    if binary.is_symlink():
        raise ValueError('Symlinked Hosting Bundle is not allowed')
    if not binary.is_file():
        raise FileNotFoundError(f'Required Hosting Bundle missing: {binary}; run scripts/fetch-windows-prerequisites.py')
    if sha512_file(binary).lower() != expected['sha512'].lower():
        raise ValueError(f'Hosting Bundle SHA512 mismatch: {binary}')
    return manifest_path, binary


def fetch_prerequisites(pin: Path, destination: Path) -> tuple[Path, Path]:
    manifest = read_manifest(pin)
    with open_official(METADATA_URL) as response:
        metadata = json.load(response)
    matches = [file for release in metadata['releases']
               if release['release-version'] == manifest['version']
               for file in release.get('aspnetcore-runtime', {}).get('files', [])
               if file['name'] == 'dotnet-hosting-win.exe']
    if (len(matches) != 1 or matches[0]['url'] != manifest['downloadUrl']
            or matches[0]['hash'].lower() != manifest['sha512'].lower()):
        raise ValueError('Pinned Hosting Bundle does not match Microsoft release metadata')
    destination.mkdir(parents=True, exist_ok=True)
    binary = destination / manifest['fileName']
    # A valid existing cache still requires the metadata check above.
    if binary.is_file() and not binary.is_symlink() and sha512_file(binary) == manifest['sha512'].lower():
        pass
    else:
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(prefix='.hosting-', dir=destination, delete=False) as handle:
                temporary = Path(handle.name)
                with open_official(manifest['downloadUrl']) as response:
                    for chunk in iter(lambda: response.read(1024 * 1024), b''):
                        handle.write(chunk)
            if sha512_file(temporary) != manifest['sha512'].lower():
                raise ValueError('Downloaded Hosting Bundle SHA512 mismatch')
            os.replace(temporary, binary)
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
    manifest_path = destination / 'hosting-bundle.json'
    if manifest_path.is_symlink():
        raise ValueError('Symlinked prerequisite manifest is not allowed')
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    return validate_cache(destination, pin)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, default=ROOT / 'prerequisites/hosting-bundle.json')
    parser.add_argument('--output-dir', type=Path, default=ROOT / 'artifacts/prerequisites')
    args = parser.parse_args()
    for path in fetch_prerequisites(args.manifest, args.output_dir):
        print(path)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
