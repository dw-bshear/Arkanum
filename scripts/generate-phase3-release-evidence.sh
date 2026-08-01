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
DOTNET_ROOT="${DOTNET_ROOT:-/home/mannie/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

MANIFEST="$OUT_DIR/arkanum-file-manifest.sha256"
PACKAGE_INVENTORY="$OUT_DIR/arkanum-package-inventory.txt"
VULN_SCAN="$OUT_DIR/arkanum-vulnerability-scan.txt"
OUTDATED_SCAN="$OUT_DIR/arkanum-outdated-scan.txt"
SBOM_DIR="$OUT_DIR/sbom"
SBOM="$SBOM_DIR/arkanum-cyclonedx-bom.xml"
LOCK_COPY_DIR="$OUT_DIR/package-locks"
CHECKSUMS="$OUT_DIR/SHA256SUMS.txt"
REPORT="$OUT_DIR/arkanum-release-evidence.txt"
mkdir -p "$SBOM_DIR" "$LOCK_COPY_DIR"

# Locked restore proves the package graph matches committed packages.lock.json files.
dotnet restore --locked-mode "$ROOT/Arkanum.sln"

(
  cd "$PUBLISH_DIR"
  find . -type f -print0 | sort -z | xargs -0 sha256sum
) > "$MANIFEST"

dotnet list "$ROOT/Arkanum.Web/Arkanum.Web.csproj" package --include-transitive > "$PACKAGE_INVENTORY"
dotnet list "$ROOT/Arkanum.sln" package --vulnerable --include-transitive > "$VULN_SCAN"
dotnet list "$ROOT/Arkanum.sln" package --outdated --include-transitive > "$OUTDATED_SCAN"

find "$ROOT" -path '*/packages.lock.json' -type f -print0 | while IFS= read -r -d '' lock_file; do
  rel="${lock_file#$ROOT/}"
  dest="$LOCK_COPY_DIR/${rel//\//__}"
  cp "$lock_file" "$dest"
done

if command -v dotnet-CycloneDX >/dev/null 2>&1; then
  dotnet-CycloneDX "$ROOT/Arkanum.sln" --output "$SBOM_DIR" --filename "$(basename "$SBOM")"
elif dotnet tool list --global 2>/dev/null | grep -qi '^cyclonedx'; then
  dotnet CycloneDX "$ROOT/Arkanum.sln" --output "$SBOM_DIR" --filename "$(basename "$SBOM")"
else
  {
    echo "CycloneDX tool not found. Install with: dotnet tool install --global CycloneDX"
    echo "Package inventory fallback: $PACKAGE_INVENTORY"
  } > "$SBOM"
fi

(
  cd "$OUT_DIR"
  find . -type f ! -name 'SHA256SUMS.txt' -print0 | sort -z | xargs -0 sha256sum
) > "$CHECKSUMS"

{
  echo "Arkanum Phase 3 release evidence"
  echo "GeneratedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "PublishDir=$PUBLISH_DIR"
  echo "FileManifest=$MANIFEST"
  echo "PackageInventory=$PACKAGE_INVENTORY"
  echo "VulnerabilityScan=$VULN_SCAN"
  echo "OutdatedScan=$OUTDATED_SCAN"
  echo "CycloneDX=$SBOM"
  echo "PackageLocks=$LOCK_COPY_DIR"
  echo "Checksums=$CHECKSUMS"
  echo "FileCount=$(find "$PUBLISH_DIR" -type f | wc -l | tr -d ' ')"
  echo "ManifestSha256=$(sha256sum "$MANIFEST" | awk '{print $1}')"
  echo "PackageInventorySha256=$(sha256sum "$PACKAGE_INVENTORY" | awk '{print $1}')"
  echo "VulnerabilityScanSha256=$(sha256sum "$VULN_SCAN" | awk '{print $1}')"
  echo "SbomSha256=$(sha256sum "$SBOM" | awk '{print $1}')"
} > "$REPORT"
echo "$REPORT"
