#!/usr/bin/env python3
"""Create Arkanum full and update release packages.

The package model intentionally emits distinct full and update assets even when the
initial update payload is a complete application copy. The distinction is a contract
for operators and installers: full packages support first install/restore/side-by-side
work, while update packages require an existing install and preserve site state.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util

import json
import os
import shutil
import tarfile
import tempfile
import zipfile
from datetime import datetime, timezone
from pathlib import Path

# Share exactly the same fail-closed validator with the online cache fetcher.
_spec = importlib.util.spec_from_file_location("windows_prerequisites", Path(__file__).with_name("fetch-windows-prerequisites.py"))
assert _spec is not None and _spec.loader is not None
_prerequisites = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_prerequisites)

ASSET_TEMPLATES = {
    ("full", "windows-x64"): "Arkanum-{version}-full-windows-x64.zip",
    ("update", "windows-x64"): "Arkanum-{version}-update-windows-x64.zip",
    ("full", "linux-x64"): "Arkanum-{version}-full-linux-x64.tar.gz",
    ("update", "linux-x64"): "Arkanum-{version}-update-linux-x64.tar.gz",
}

PRESERVED_UPDATE_STATE = [
    "configuration",
    "recovery-master-key",
    "database-connectivity-settings",
    "service-binding-settings",
]


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def copy_tree(src: Path, dst: Path) -> None:
    if not src.exists():
        raise FileNotFoundError(f"Required package input is missing: {src}")
    if src.is_dir():
        shutil.copytree(src, dst, dirs_exist_ok=True, ignore=shutil.ignore_patterns('__pycache__', '*.pyc'))
    else:
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)


def write_manifest(stage: Path, version: str, package_kind: str, platform: str, minimum_installed_version: str | None) -> None:
    manifest = {
        "product": "Arkanum",
        "version": version,
        "packageKind": package_kind,
        "platform": platform,
        "requiresExistingInstall": package_kind == "update",
        "minimumInstalledVersion": minimum_installed_version if package_kind == "update" else None,
        "preserves": PRESERVED_UPDATE_STATE if package_kind == "update" else [],
        "createdAtUtc": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    }
    (stage / "package-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


def stage_package(root: Path, publish_dir: Path, stage: Path, version: str, package_kind: str, platform: str, minimum_installed_version: str | None, windows_prerequisites: Path | None = None) -> None:
    if platform == "windows-x64":
        directory = windows_prerequisites if windows_prerequisites is not None else root / "artifacts/prerequisites"
        for source in _prerequisites.validate_cache(directory, root / "prerequisites/hosting-bundle.json"):
            copy_tree(source, stage / "prerequisites" / source.name)
    app_dir = stage / "app"
    copy_tree(publish_dir, app_dir)
    copy_tree(root / "installers", stage / "installers")
    copy_tree(root / "docs", stage / "docs")
    for name in ["README.md", "Directory.Build.props", "packages.lock.json"]:
        source = root / name
        if source.exists():
            copy_tree(source, stage / name)
    write_manifest(stage, version, package_kind, platform, minimum_installed_version)


def make_zip(stage: Path, out_path: Path) -> None:
    with zipfile.ZipFile(out_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        for file_path in sorted(p for p in stage.rglob("*") if p.is_file()):
            archive.write(file_path, file_path.relative_to(stage).as_posix())


def make_tar(stage: Path, out_path: Path) -> None:
    with tarfile.open(out_path, "w:gz") as archive:
        for file_path in sorted(stage.rglob("*")):
            archive.add(file_path, arcname=file_path.relative_to(stage).as_posix(), recursive=False)


def build_assets(args: argparse.Namespace) -> list[Path]:
    root = Path(args.root).resolve()
    out_dir = Path(args.out_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    publish_dirs = {
        "windows-x64": Path(args.windows_publish).resolve(),
        "linux-x64": Path(args.linux_publish).resolve(),
    }
    created: list[Path] = []
    for (package_kind, platform), template in ASSET_TEMPLATES.items():
        file_name = template.format(version=args.version)
        out_path = out_dir / file_name
        with tempfile.TemporaryDirectory(prefix="arkanum-package-") as tmp:
            stage = Path(tmp) / file_name.removesuffix(".zip").removesuffix(".tar.gz")
            stage.mkdir(parents=True)
            prerequisite_dir = getattr(args, "windows_prerequisites", None)
            stage_package(root, publish_dirs[platform], stage, args.version, package_kind, platform, args.minimum_installed_version,
                          Path(prerequisite_dir).resolve() if prerequisite_dir is not None else None)
            if out_path.suffix == ".zip":
                make_zip(stage, out_path)
            else:
                make_tar(stage, out_path)
        checksum = sha256_file(out_path)
        out_path.with_suffix(out_path.suffix + ".sha256").write_text(f"{checksum}  {out_path.name}\n", encoding="utf-8")
        created.extend([out_path, out_path.with_suffix(out_path.suffix + ".sha256")])
    return created


def main() -> int:
    parser = argparse.ArgumentParser(description="Create Arkanum full/update release assets with package manifests.")
    parser.add_argument("--version", required=True)
    parser.add_argument("--minimum-installed-version", default=None)
    parser.add_argument("--root", default=Path(__file__).resolve().parents[1])
    parser.add_argument("--windows-publish", default="publish/arkanum-windows-x64")
    parser.add_argument("--linux-publish", default="publish/arkanum-linux-x64")
    parser.add_argument("--out-dir", default="artifacts/release")
    parser.add_argument("--windows-prerequisites", default="artifacts/prerequisites",
                        help="Directory containing the real SHA512-validated Hosting Bundle and hosting-bundle.json")
    args = parser.parse_args()
    created = build_assets(args)
    for path in created:
        print(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
