# Offline release evidence bundle

This guide defines the Arkanum evidence bundle expected for controlled, disconnected, and air-gapped review. The bundle is intended to support a customer RMF/ATO package with review-ready evidence; it is not a standalone accreditation or certification claim.

## Bundle layout

Create one evidence directory per release and keep artifact names stable enough for transfer administrators and assessors to compare across releases.

```text
artifacts/rmf-evidence-bundle/
  README.txt
  SHA256SUMS.txt
  compliance/
    rmf-stig-evidence-matrix.md
    audit-compliance-evidence.md
    installer-runtime-hardening.md
    offline-release-evidence-bundle.md
  release-evidence/
    arkanum-release-evidence.txt
    arkanum-file-manifest.sha256
    arkanum-package-inventory.txt
    arkanum-vulnerability-scan.txt
    arkanum-outdated-scan.txt
    sbom/
    package-locks/
  transcripts/
    install-transcript.txt
    backup-restore-transcript.txt
  signing/
    signing-evidence.txt
  operations/
    backup-and-restore.md
    offline-package-checklist.md
    vault-export-validation.md
```

The repeatable collection entry point is `scripts/generate-rmf-evidence-bundle.sh`. It calls `scripts/generate-phase3-release-evidence.sh` when a publish directory is available, then stages compliance docs, transcripts, signing evidence, and operational procedures into a checksummed bundle. Run the script from the release workspace with trusted local paths only; it copies operator-supplied transcript and signing files verbatim after the operator has redacted and approved them.

## Required contents

| Requirement | Bundle item | Notes |
| --- | --- | --- |
| signed artifacts or signing plan | `signing/signing-evidence.txt` | Include Authenticode/codesign/GPG evidence when available. If signing is not yet operational, include the approved signing plan, owner, target certificate store, and interim transfer control. |
| SBOM | `release-evidence/sbom/` | CycloneDX is preferred. If the generator is unavailable, include `CYCLONEDX_MISSING.txt` plus package inventory as fallback evidence until the tool is installed. |
| checksums | top-level `SHA256SUMS.txt`; release file manifests | Top-level checksums cover the evidence bundle; release manifests cover the published application package. Verify again after offline transfer. |
| vulnerability report | `release-evidence/arkanum-vulnerability-scan.txt` | Generated with `dotnet list Arkanum.sln package --vulnerable --include-transitive` for the exact release commit. |
| install transcript | `transcripts/install-transcript.txt` | Capture the installer command, redacted output, `/health`, `/install-report`, IIS/site binding checks, SQL connectivity, and first-admin setup result. |
| backup/restore transcript | `transcripts/backup-restore-transcript.txt` | Capture SQL backup, recovery-key handling, restore command, post-restore `/health`, and a non-production vault reveal/copy validation where permitted. |
| hardening guide | `compliance/installer-runtime-hardening.md` | Include protected configuration, least-privilege runtime SQL login, setup/migration identity, and redaction expectations. |
| control evidence matrix | `compliance/rmf-stig-evidence-matrix.md` | Include implemented, partial, inherited, planned, and not applicable statuses. |
| audit evidence guide | `compliance/audit-compliance-evidence.md` | Include append-only/hash-chain, retention, forwarding, and archive/export/restore expectations. |

## Air-gapped update/patch process

1. Build from the approved source commit and record the commit, version, build host, SDK/runtime, and package names.
2. Run locked restore and dependency checks before packaging:

   ```bash
   dotnet restore --locked-mode Arkanum.sln
   dotnet list Arkanum.sln package --vulnerable --include-transitive
   dotnet list Arkanum.sln package --outdated --include-transitive
   ```

3. Generate release evidence and the RMF bundle:

   ```bash
   scripts/generate-rmf-evidence-bundle.sh publish/current artifacts/rmf-evidence-bundle
   ```

4. Hash the package archives and evidence bundle. Sign artifacts, or attach the approved signing plan if signing is not yet available.
5. Transfer packages and evidence through the approved removable-media, data-diode, or transfer-portal process.
6. On the receiving side, verify every checksum before install or upgrade.
7. Run the installer in upgrade mode during the approved maintenance window. Preserve the pre-upgrade database backup and recovery key separately from the application package.
8. Capture install transcript, `/health`, `/install-report`, post-upgrade login, audit-forwarding status, and rollback instructions.
9. File the release evidence with the change ticket and RMF/ATO evidence repository.

## Offline NuGet/cache process

1. Treat `packages.lock.json` files as the approved dependency graph for a release.
2. Mirror only the approved package IDs and versions into the disconnected NuGet feed or removable cache.
3. Configure the build host inside the receiving network to restore from that approved feed/cache, not from the public internet.
4. Run `dotnet restore --locked-mode Arkanum.sln` against the offline cache before publishing.
5. Save the NuGet source configuration, lock files, package inventory, SBOM, and vulnerability report in the evidence bundle.
6. If a package must be changed to satisfy a vulnerability finding, update the source lock files in the connected development environment, rerun tests, create a new release commit, and regenerate the evidence bundle.

## Vulnerability exception process

1. Review `release-evidence/arkanum-vulnerability-scan.txt` and external scanner output for the exact release build.
2. For every finding that is not remediated before release, document:
   - package/component and version,
   - vulnerability identifier,
   - exploitability within the Arkanum deployment boundary,
   - compensating controls or inherited controls,
   - expiration date,
   - approving risk owner,
   - follow-up issue or patch target.
3. Store exceptions with the release evidence and the organization's POA&M or risk register.
4. Do not silently reuse an exception for a later release; revalidate against the new dependency graph and deployment boundary.

## Transcript guidance

Transcripts must be redacted before entering the evidence bundle. Do not include SQL passwords, LDAP bind passwords, SMTP credentials, recovery/master keys, initial administrator passwords, private keys, or plaintext vault contents. It is acceptable to record non-secret paths, command names, hostnames, status codes, package filenames, and checksum values.

## Related docs

- `docs/rmf-stig-evidence-matrix.md`
- `docs/installer-runtime-hardening.md`
- `docs/audit-compliance-evidence.md`
- `docs/backup-and-restore.md`
- `docs/offline-package-checklist.md`
- `docs/vault-export-validation.md`
