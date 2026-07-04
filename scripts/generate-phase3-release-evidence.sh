#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="${1:-$ROOT/publish/current}"
OUT_DIR="${2:-$ROOT/artifacts/phase3-release-evidence}"
mkdir -p "$OUT_DIR"
if [[ ! -d "$PUBLISH_DIR" ]]; then
  echo "Publish directory not found: $PUBLISH_DIR" >&2
  exit 2
fi
MANIFEST="$OUT_DIR/arkanum-file-manifest.sha256"
SBOM="$OUT_DIR/arkanum-package-inventory.txt"
REPORT="$OUT_DIR/arkanum-release-evidence.txt"
(
  cd "$PUBLISH_DIR"
  find . -type f -print0 | sort -z | xargs -0 sha256sum
) > "$MANIFEST"
{
  echo "Arkanum package inventory"
  echo "GeneratedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo
  DOTNET_ROOT="${DOTNET_ROOT:-/home/mannie/.dotnet}"
  PATH="$DOTNET_ROOT:$PATH" dotnet list "$ROOT/Arkanum.Web/Arkanum.Web.csproj" package --include-transitive
} > "$SBOM"
{
  echo "Arkanum Phase 3 release evidence"
  echo "GeneratedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "PublishDir=$PUBLISH_DIR"
  echo "FileManifest=$MANIFEST"
  echo "PackageInventory=$SBOM"
  echo "FileCount=$(find "$PUBLISH_DIR" -type f | wc -l | tr -d ' ')"
  echo "ManifestSha256=$(sha256sum "$MANIFEST" | awk '{print $1}')"
  echo "PackageInventorySha256=$(sha256sum "$SBOM" | awk '{print $1}')"
} > "$REPORT"
echo "$REPORT"
