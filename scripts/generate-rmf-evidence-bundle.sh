#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="${1:-$ROOT/publish/current}"
OUT_DIR="${2:-$ROOT/artifacts/rmf-evidence-bundle}"
INSTALL_TRANSCRIPT="${INSTALL_TRANSCRIPT:-}"
BACKUP_RESTORE_TRANSCRIPT="${BACKUP_RESTORE_TRANSCRIPT:-}"
SIGNING_EVIDENCE="${SIGNING_EVIDENCE:-}"

COMPLIANCE_DIR="$OUT_DIR/compliance"
OPERATIONS_DIR="$OUT_DIR/operations"
TRANSCRIPTS_DIR="$OUT_DIR/transcripts"
SIGNING_DIR="$OUT_DIR/signing"
RELEASE_EVIDENCE_DIR="$OUT_DIR/release-evidence"
CHECKSUMS="$OUT_DIR/SHA256SUMS.txt"
README="$OUT_DIR/README.txt"

mkdir -p "$COMPLIANCE_DIR" "$OPERATIONS_DIR" "$TRANSCRIPTS_DIR" "$SIGNING_DIR" "$RELEASE_EVIDENCE_DIR"

copy_required() {
  local source="$1"
  local destination="$2"
  if [[ ! -f "$source" ]]; then
    echo "Required evidence source missing: $source" >&2
    exit 2
  fi
  cp "$source" "$destination"
}

copy_optional_or_placeholder() {
  local source="$1"
  local destination="$2"
  local placeholder="$3"
  if [[ -n "$source" && -f "$source" ]]; then
    cp "$source" "$destination"
  else
    printf '%s\n' "$placeholder" > "$destination"
  fi
}

copy_required "$ROOT/docs/rmf-stig-evidence-matrix.md" "$COMPLIANCE_DIR/rmf-stig-evidence-matrix.md"
copy_required "$ROOT/docs/offline-release-evidence-bundle.md" "$COMPLIANCE_DIR/offline-release-evidence-bundle.md"
copy_required "$ROOT/docs/audit-compliance-evidence.md" "$COMPLIANCE_DIR/audit-compliance-evidence.md"
copy_required "$ROOT/docs/installer-runtime-hardening.md" "$COMPLIANCE_DIR/installer-runtime-hardening.md"
copy_required "$ROOT/docs/backup-and-restore.md" "$OPERATIONS_DIR/backup-and-restore.md"
copy_required "$ROOT/docs/offline-package-checklist.md" "$OPERATIONS_DIR/offline-package-checklist.md"
copy_required "$ROOT/docs/vault-export-validation.md" "$OPERATIONS_DIR/vault-export-validation.md"

if [[ -d "$PUBLISH_DIR" ]]; then
  "$ROOT/scripts/generate-phase3-release-evidence.sh" "$PUBLISH_DIR" "$RELEASE_EVIDENCE_DIR" >/dev/null
else
  {
    echo "Publish directory not found: $PUBLISH_DIR"
    echo "Provide a publish directory as the first argument to collect manifests, SBOM, package inventory, and vulnerability reports."
  } > "$RELEASE_EVIDENCE_DIR/PUBLISH_DIR_MISSING.txt"
fi

copy_optional_or_placeholder \
  "$INSTALL_TRANSCRIPT" \
  "$TRANSCRIPTS_DIR/install-transcript.txt" \
  "INSTALL_TRANSCRIPT was not provided. Capture redacted installer output, /health, /install-report, IIS binding checks, SQL connectivity, and first-admin setup before release approval."

copy_optional_or_placeholder \
  "$BACKUP_RESTORE_TRANSCRIPT" \
  "$TRANSCRIPTS_DIR/backup-restore-transcript.txt" \
  "BACKUP_RESTORE_TRANSCRIPT was not provided. Capture SQL backup, recovery-key handling, restore command, post-restore /health, and non-production reveal/copy validation before release approval."

copy_optional_or_placeholder \
  "$SIGNING_EVIDENCE" \
  "$SIGNING_DIR/signing-evidence.txt" \
  "SIGNING_EVIDENCE was not provided. Attach signed artifacts or signing plan with owner, certificate/source, timestamping approach, and interim transfer control."

{
  echo "Arkanum RMF/STIG offline release evidence bundle"
  echo "GeneratedAt=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "PublishDir=$PUBLISH_DIR"
  echo "ComplianceMatrix=docs/rmf-stig-evidence-matrix.md"
  echo "BundleGuide=docs/offline-release-evidence-bundle.md"
  echo "ReleaseEvidence=release-evidence/arkanum-release-evidence.txt"
  echo "InstallTranscript=transcripts/install-transcript.txt"
  echo "BackupRestoreTranscript=transcripts/backup-restore-transcript.txt"
  echo "SigningEvidence=signing/signing-evidence.txt"
  echo "Checksums=SHA256SUMS.txt"
  echo "Purpose=review-ready evidence for the customer RMF/ATO package"
} > "$README"

(
  cd "$OUT_DIR"
  find . -type f ! -name 'SHA256SUMS.txt' -print0 | sort -z | xargs -0 sha256sum
) > "$CHECKSUMS"

echo "$OUT_DIR"
