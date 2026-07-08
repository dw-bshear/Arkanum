# Conclave

Conclave is a limited internal-use credential vault build derived from Arkanum 2026.0.6.

This branch is a frozen evaluation/support fork created as a separate deliverable for a larger testing partner. Conclave is not the active Arkanum product line and does not imply rights to future Arkanum releases, updates, branding, source code outside this fork, or support unless separately agreed in writing.

Arkanum remains the active Darkwing-IT LLC product line.

> Conclave is designed for controlled and disconnected networks. This repository/branch does not claim DISA approval, STIG compliance, FIPS certification, FedRAMP readiness, RMF/ATO completion, or any other formal certification.

## Version

```text
Conclave 2026.0.1
Derived from Arkanum 2026.0.6
Branch: conclave
Intended status: frozen internal-use fork
```

## Current capabilities

- Vaults, folders, login credentials, and secure notes.
- Collapsed-by-default vault trees so entry names are not exposed until a user expands the relevant folder/subtree.
- Separate Credentials and Secure Notes subtrees inside each vault folder, with alphabetized entries.
- Inline reveal/copy flow with masked placeholders until an authorized reveal/copy action is selected.
- Per-entry reset notification dates for credential or secure-note review/change reminders.
- Controlled CSV export for vault contents using encrypted ZIP payloads.
- Server-side AES-256-GCM encryption for protected vault item content.
- Site-wide recovery key model: all vaults recover together, or encrypted contents do not recover.
- Installer restore mode for rebuilding a server from a SQL backup plus the matching Conclave Recovery Key.
- First-run local administrator setup.
- Separate federated login and emergency/manual login pages.
- Local emergency administrator accounts with hashed passwords.
- Optional OIDC federation readiness with existing-user gating.
- LDAP/AD import settings and directory-sync foundation.
- Role-aware dashboard and access boundaries.
- Audit events for logins, lockouts, settings changes, vault assignments, and secret reveal/copy actions.
- Syslog-forwarding foundation and non-secret health/install-report endpoints.

## License / use boundary

Conclave is intended as a one-time internal-use fork. Recommended agreement language should make clear that:

- Conclave is separate from Arkanum.
- Rights apply only to the Conclave build/branch delivered here.
- Future Arkanum releases are not included automatically.
- Redistribution, resale, sublicensing, or public hosting requires separate written permission.
- Ongoing support, security updates, or maintenance require a separate agreement.
- Feedback from Conclave testing may be used to improve Arkanum.

## Security and recovery model

Conclave uses a trusted-server model:

1. SQL Server stores users, settings, audit events, vault metadata, and encrypted vault item content.
2. The Conclave application server holds the runtime configuration and site recovery/master key inside the trusted boundary.
3. Authorized reveal/copy actions decrypt protected values server-side and write audit events.
4. Plaintext secrets should never be logged, exported in install reports, or committed to source control.

Recoverable state requires both:

```text
SQL Server backup + matching Conclave recovery/master key
```

A SQL Server backup alone is not sufficient to decrypt existing credentials or secure notes.

## Installation targets

The intended feedback/deployment tracks are:

1. **Windows Server 2022 / IIS / SQL Server**
   - Primary enterprise feedback path.
   - Uses a published Conclave web package, IIS, a dedicated app pool identity, and SQL Server or SQL Server Express.

2. **Linux / systemd / SQL Server**
   - Secondary supported path for Linux server teams.
   - Uses a published Conclave web package, a locked-down `conclave` service account, systemd, and an external or local SQL Server endpoint.

3. **Containerized SQL or app hosting**
   - Lab convenience only.

See:

- [`docs/conclave-2026.0.1-release.md`](docs/conclave-2026.0.1-release.md)
- [`docs/upgrade-and-native-packaging.md`](docs/upgrade-and-native-packaging.md)
- [`installers/windows/`](installers/windows/)
- [`installers/linux/`](installers/linux/)

## Local development

### Requirements

- .NET SDK 9.0
- SQL Server for durable/review deployments
- Optional: local SQLite fallback for lightweight review/dev flows

### Restore, build, and test

```bash
dotnet restore
dotnet build
dotnet test
```

### Run locally

```bash
export CONCLAVE_MASTER_KEY="replace-with-local-dev-key-material"
dotnet run --project Arkanum.Web --urls http://127.0.0.1:5068
```

For SQL-backed review, provide a SQL Server connection string through environment configuration instead of committing secrets:

```bash
export CONCLAVE_MASTER_KEY="replace-with-site-recovery-key"
export ConnectionStrings__DefaultConnection="Server=tcp:sql01,1433;Database=Conclave;User Id=conclave_app;Password=...;Encrypt=True;TrustServerCertificate=True;"
dotnet run --project Arkanum.Web --urls http://127.0.0.1:5068
```

Do not commit real SQL passwords, LDAP bind passwords, recovery keys, databases, runtime config, or local publish output.

## Health and install report

Conclave exposes non-secret operational endpoints:

- `GET /health`
- `GET /install-report`

`/health` returns HTTP 200 when healthy and HTTP 503 when unhealthy. It checks application responsiveness, database reachability, required settings, encryption-key availability, and recovery-key/database compatibility.

`/install-report` summarizes health, counts, operational settings, federation readiness, and security-control evidence without exposing protected values.

## Documentation map

| Document | Purpose |
| --- | --- |
| [`docs/conclave-2026.0.1-release.md`](docs/conclave-2026.0.1-release.md) | Conclave frozen-fork release notes. |
| [`docs/backup-and-restore.md`](docs/backup-and-restore.md) | SQL database + recovery/master-key restore model. |
| [`docs/offline-package-checklist.md`](docs/offline-package-checklist.md) | Checklist for moving packages into controlled/disconnected networks. |

## Status

Conclave is a frozen fork for internal-use evaluation. Active product development continues on Arkanum, not Conclave.
