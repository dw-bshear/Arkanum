# Arkanum Phase 1 installable feedback release

Phase 1 turns Arkanum from a LAN review build into a repeatable install candidate for controlled and air-gapped networks. The goal is not a certification claim; the goal is a defensible package that administrators can install, verify, back up, and give feedback on without reverse-engineering the development workstation.

## Release tracks

1. **Windows Server 2022 / IIS / SQL Server**
   - Primary enterprise feedback path.
   - Uses a published Arkanum web package, IIS, a dedicated app pool identity, and SQL Server or SQL Server Express.

2. **Linux / systemd / SQL Server**
   - Secondary supported path for Linux server teams.
   - Uses a published Arkanum web package, a locked-down `arkanum` service account, systemd, and an external or local SQL Server endpoint.

3. **Containerized SQL or app hosting**
   - Optional convenience for labs only.
   - Not the only supported install path because many air-gapped networks restrict containers.

## Phase 1 must-haves

- Published release package; target systems should not need the .NET SDK or source tree.
- SQL Server setup/validation path.
- Least-privilege runtime SQL credential guidance.
- Durable master encryption key generation/import guidance.
- First local administrator setup.
- Production-safe `/health` endpoint.
- Windows and Linux installer skeletons.
- Backup/restore documentation.
- Offline package checklist and checksums.
- Production error behavior: no detailed exception disclosure to end users.

## Phase 1 does not claim

- DISA approval.
- FedRAMP readiness.
- STIG compliance.
- FIPS certification.
- RMF/ATO completion.

Use language such as: "designed for controlled and disconnected networks" rather than certification claims until evidence exists.

## Operational boundary

Arkanum is currently a trusted-server credential vault. The application server decrypts vault secrets only for explicit authorized reveal/copy actions, and those actions must be audited. Therefore the app server runtime identity, SQL runtime credential, and master encryption key are inside the trusted boundary.

## Phase 1 health signal

`GET /health` returns a non-secret JSON status and HTTP 200 when healthy or HTTP 503 when unhealthy. It checks:

- application process responsiveness;
- database connectivity and required tables;
- required portal setting defaults;
- master encryption key availability.

The endpoint intentionally avoids returning connection strings, passwords, filesystem paths, stack traces, or plaintext secrets.

## Next implementation priorities

1. Replace prototype schema creation with EF Core migrations or an explicit installer-driven schema setup step.
2. Expand install scripts from skeletons into full idempotent installers.
3. Add install report generation without secrets.
4. Add release packaging scripts for Windows ZIP and Linux tarball.
5. Add SBOM/checksum generation.
