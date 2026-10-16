#!/usr/bin/env python3
"""Fail-closed Arkanum 2026.1.2 evidence packaging, not a scanner or certification.

Scanner and PDF/schema validation must be performed separately and recorded.
Only reviewed, redacted evidence belongs in the input directory.
"""
import argparse
from datetime import datetime
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat
import zipfile

SOURCE_COMMIT = '85961108e7df8c043726eca38490ad6e733db5e5'
REQUIRED = (
    'README.md', 'manifest.json', 'evidence/execution-manifest.json',
    'reports/software-security-assessment.md', 'reports/software-security-assessment.pdf',
    'reports/vulnerability-and-testing-report.md', 'reports/installation-hardening-guide.md',
    'reports/control-applicability-matrix.md', 'reports/release-integrity-and-provenance.md',
    'reports/findings-and-limitations.md', 'sbom/windows-app.cdx.json',
    'sbom/linux-app.cdx.json', 'sbom/release-distribution.cdx.json',
)
MANDATORY_CHECKS = (
    'syft-windows', 'syft-linux', 'grype-windows', 'grype-linux', 'semgrep', 'gitleaks',
    'nuget-audit', 'dotnet-tests', 'python-tests', 'sql-smoke', 'schema-validation', 'pdf-validation',
)
STATUSES = {'passed', 'failed', 'completed-with-findings', 'unavailable', 'not-run'}
CONFIDENTIAL = re.compile(
    r'/home/[^\s\"<>]+|/srv/blackbox/|-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----|'
    r'ark-recovery-v1\.[A-Za-z0-9_+/=-]+|'
    r'(?i:"(?:password|clientSecret|recoveryKey|privateKey)"\s*:\s*")(?!\[REDACTED\]|<|"|null)[^"\r\n]+|'
    r'(?i:Password\s*[=:]\s*)(?!\[REDACTED\]|<[^>]*>)[^;\s\"\']+|'
    r'\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3}|'
    r'172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3})\b'
)


def safe_path(name):
    p = PurePosixPath(name)
    if not name or p.is_absolute() or '\\' in name or any(x in {'', '.', '..'} for x in name.split('/')) or ':' in name or any(ord(x) < 32 for x in name):
        raise ValueError('Unsafe evidence member path')
    if any(x.lower() in {'.git', '__pycache__', 'id_rsa', 'id_ed25519'} or x.lower().startswith('.env') for x in p.parts) or p.suffix.lower() in {'.key', '.pem', '.pfx', '.db', '.bak', '.dll', '.exe', '.pyc'}:
        raise ValueError('Forbidden evidence member type')
    return p


def utc(value):
    if not isinstance(value, str) or not value.endswith('Z'):
        raise ValueError('Timestamp must explicitly use UTC Z')
    try:
        return datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError as exc:
        raise ValueError('Invalid UTC timestamp') from exc


def validate_metadata(files):
    if not set(REQUIRED) <= set(files):
        raise ValueError('Missing mandatory evidence: ' + ', '.join(sorted(set(REQUIRED) - set(files))))
    try:
        manifest = json.loads(files['manifest.json'])
        release = manifest['release']
        if release != dict(version='2026.1.2', tag='v2026.1.2', sourceCommit=SOURCE_COMMIT):
            raise ValueError('Immutable release linkage mismatch')
        if not re.fullmatch('[0-9a-f]{40}', manifest['evidenceToolingCommit']):
            raise ValueError('Final evidence requires actual committed tooling identity')
        if manifest['assessmentType'] != 'developer-prepared; not independent government authorization':
            raise ValueError('Assessment boundary statement mismatch')
        utc(manifest['generatedAtUtc'])
        executions = json.loads(files['evidence/execution-manifest.json'])['executions']
        ids = [x['id'] for x in executions]
        if len(set(ids)) != len(ids) or not set(MANDATORY_CHECKS) <= set(ids):
            raise ValueError('Missing or duplicate execution record')
        for record in executions:
            for field in ('command', 'version', 'scope', 'outputPath'):
                if not isinstance(record[field], str) or not record[field].strip():
                    raise ValueError('Missing execution metadata')
            safe_path(record['outputPath'])
            if record['outputPath'] not in files:
                raise ValueError('Execution output does not exist')
            status, code = record['status'], record['exitStatus']
            if status not in STATUSES or (code is not None and (isinstance(code, bool) or not isinstance(code, int))):
                raise ValueError('Invalid execution status/code')
            if status == 'passed' and code != 0:
                raise ValueError('Failed command cannot be passed')
            if status == 'failed' and (code is None or code == 0):
                raise ValueError('Failure requires actual nonzero exit')
            if status == 'completed-with-findings' and code not in (0, 1):
                raise ValueError('Scanner failure cannot be findings-only')
            if status == 'not-run' and code is not None:
                raise ValueError('Not-run check cannot have exit status')
            if utc(record['endedAtUtc']) < utc(record['startedAtUtc']):
                raise ValueError('Execution ends before it starts')
    except (KeyError, TypeError, json.JSONDecodeError) as exc:
        raise ValueError('Malformed evidence metadata') from exc


def validate_tree(root):
    root = Path(root)
    if root.is_symlink() or not root.is_dir():
        raise ValueError('Evidence root must be a real directory')
    files = {}
    for path in sorted(root.rglob('*')):
        if path.is_symlink():
            raise ValueError('Symlinks forbidden')
        if path.is_dir():
            continue
        name = path.relative_to(root).as_posix()
        safe_path(name)
        if not path.is_file():
            raise ValueError('Only regular evidence files allowed')
        if name == 'SHA256SUMS.txt':
            continue
        content = path.read_bytes()
        # Binary PDF requires separate extracted-text/visual validation in execution manifest.
        if path.suffix.lower() != '.pdf':
            try:
                text = content.decode('utf-8')
            except UnicodeDecodeError as exc:
                raise ValueError('Unreviewed binary evidence') from exc
            if CONFIDENTIAL.search(text):
                raise ValueError('Confidential material detected in ' + name)
        files[name] = content
    validate_metadata(files)
    return files


def package(root, output):
    files = validate_tree(root)
    sums = ''.join(hashlib.sha256(data).hexdigest() + '  ' + name + '\n' for name, data in sorted(files.items()))
    (Path(root) / 'SHA256SUMS.txt').write_text(sums, encoding='utf-8')
    files['SHA256SUMS.txt'] = sums.encode()
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    archive = output / 'Arkanum-2026.1.2-Security-Evidence.zip'
    if archive.exists():
        raise ValueError('Refusing to overwrite an existing evidence ZIP')
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
        for name, data in sorted(files.items()):
            z.writestr(name, data)
    verify_zip(archive)
    Path(str(archive) + '.sha256').write_text(hashlib.sha256(archive.read_bytes()).hexdigest() + '  ' + archive.name + '\n', encoding='utf-8')
    return archive


def verify_zip(archive):
    with zipfile.ZipFile(archive) as z:
        names = z.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate ZIP member')
        for info in z.infolist():
            safe_path(info.filename)
            if stat.S_ISLNK(info.external_attr >> 16) or info.is_dir():
                raise ValueError('Non-file ZIP member')
        files = {name: z.read(name) for name in names}
        validate_metadata(files)
        if 'SHA256SUMS.txt' not in files:
            raise ValueError('Checksum inventory missing')
        expected = {}
        for line in files['SHA256SUMS.txt'].decode().splitlines():
            match = re.fullmatch(r'([0-9a-f]{64})  (.+)', line)
            if not match:
                raise ValueError('Malformed checksum entry')
            digest, name = match.groups()
            safe_path(name)
            if name in expected or name == 'SHA256SUMS.txt':
                raise ValueError('Duplicate or self checksum')
            expected[name] = digest
        if set(expected) != set(files) - {'SHA256SUMS.txt'}:
            raise ValueError('Incomplete checksum coverage')
        for name, digest in expected.items():
            if hashlib.sha256(files[name]).hexdigest() != digest:
                raise ValueError('Evidence checksum mismatch: ' + name)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    build = sub.add_parser('package')
    build.add_argument('--input', required=True, type=Path)
    build.add_argument('--output', required=True, type=Path)
    check = sub.add_parser('verify')
    check.add_argument('archive', type=Path)
    args = parser.parse_args()
    try:
        if args.action == 'package':
            print(package(args.input, args.output))
        else:
            verify_zip(args.archive)
            print('Verified unique safe ZIP members and every member SHA256')
    except (ValueError, OSError, zipfile.BadZipFile) as exc:
        parser.exit(2, str(exc) + '\n')


if __name__ == '__main__':
    main()
