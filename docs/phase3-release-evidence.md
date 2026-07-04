# Arkanum Phase 3 release evidence and installation verification

Phase 3 turns the hardened Phase 2 preview into a reviewable release candidate for controlled and disconnected networks. It still avoids unsupported certification claims. The goal is repeatable installation evidence: health, configuration posture, file checksums, dependency inventory, and operational readiness notes.

## Completed in this Phase 3 slice

- Application version advanced to `phase3-preview` in `/health` and `/install-report`.
- Added `/install-report`, a JSON installation evidence report intended for administrators/installers.
- Added release-evidence generation script:

```text
scripts/generate-phase3-release-evidence.sh
```

The script writes:

```text
artifacts/phase3-release-evidence/arkanum-file-manifest.sha256
artifacts/phase3-release-evidence/arkanum-package-inventory.txt
artifacts/phase3-release-evidence/arkanum-release-evidence.txt
```

## Installation report contents

The installation report includes:

- product/version
- generated timestamp
- runtime framework and operating system description
- database provider
- health status and health checks
- user/vault/item/audit counts
- security-control posture:
  - security headers enabled
  - sensitive page no-store enabled
  - auth cookie HttpOnly / SameSite / secure-policy mode
  - local-account lockout threshold and duration
  - syslog configuration presence
- operational settings posture:
  - LDAP configured
  - mail configured
  - syslog enabled
  - SQL Server settings configured
  - audit retention window

The report intentionally does not include plaintext secrets, connection strings, bind passwords, SMTP credentials, master-key material, vault item secrets, or password hashes.

## Release evidence files

The Phase 3 release-evidence script creates:

- a SHA-256 manifest for every file in the published output
- a package/dependency inventory from `dotnet list package --include-transitive`
- a short evidence index with file counts and checksums of the evidence artifacts themselves

These artifacts are a starting point for offline transfer review and repeatable install validation. A later production packaging pass should sign the manifests, include installer logs, and produce a formal SBOM format such as SPDX or CycloneDX if the target review process requires it.

## Remaining Phase 3 work

- Windows Server 2022/IIS installation script and generated install transcript.
- FIPS-mode compatibility test notes for Windows Server and hardened Linux hosts.
- Offline package/signing workflow.
- Backup/restore verification transcript.
- Formal STIG/RMF control mapping only after evidence exists; do not claim compliance from this preview alone.
