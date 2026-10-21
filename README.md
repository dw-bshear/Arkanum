# Arkanum

Arkanum is an internal credential vault for controlled, disconnected, or air-gapped IT environments. It is designed to give administrators and authorized users a structured place to manage vaults, folders, login credentials, and secure notes while preserving auditability around access to protected content.

Arkanum is currently a trusted-server ASP.NET Core application backed by SQL Server. The application server decrypts vault items only for explicit, authorized reveal/copy actions, and those actions are recorded as audit events. The SQL database alone is not sufficient to recover protected vault contents; restore requires the matching site recovery/master key.

> Arkanum is designed for controlled and disconnected networks. This repository does not claim DISA approval, STIG compliance, FIPS certification, FedRAMP readiness, RMF/ATO completion, or any other formal certification.

## Current capabilities

- Vaults, folders, login credentials, and secure notes.
- Collapsed-by-default vault trees so entry names are not exposed until a user expands the relevant folder/subtree.
- Separate Credentials and Secure Notes subtrees inside each vault folder, with alphabetized entries.
- Inline reveal/copy flow with masked placeholders until an authorized reveal/copy action is selected.
- Per-entry reset notification dates for credential or secure-note review/change reminders.
- Controlled CSV export for vault contents using encrypted ZIP payloads.
- Vault export validation uses authenticated AES-GCM package encryption with a documented tamper-detection path in [`docs/vault-export-validation.md`](docs/vault-export-validation.md).
- Server-side AES-256-GCM encryption for protected vault item content.
- Site-wide recovery key model: all vaults recover together, or encrypted contents do not recover.
- Installer restore mode for rebuilding a server from a SQL backup plus the matching Arkanum Recovery Key.
- First-run local administrator setup.
- Separate federated login and emergency/manual login pages.
- Local emergency administrator accounts with hashed passwords.
- Optional OIDC federation readiness with existing-user gating.
- LDAP/AD import settings and directory-sync foundation.
- Imported directory users disabled by default until an administrator activates them.
- Role-aware dashboard and access boundaries.
- Audit events for logins, lockouts, settings changes, vault assignments, and secret reveal/copy actions, with append-only application controls and hash chain integrity metadata.
- TLS syslog, Windows Event Log, or approved SIEM forwarder audit-delivery paths with non-secret health/install-report endpoints.
- Windows Server/IIS and Linux/systemd installer tracks in progress.
- Windows PowerShell 5.1 setup with consent-based IIS preparation and an offline Microsoft .NET 10 Hosting Bundle; no separate Windows `sqlcmd` installation.

## Roles

| Role | Intended use |
| --- | --- |
| Administrator | Full system administration: settings, users, roles, directory, health/evidence review, and vault oversight. |
| Vault Manager | Operational vault stewardship: create and manage vaults, folders, access, and entries without system-wide settings access. |
| Auditor | Read-only oversight: review vault inventory and audit history without revealing or changing protected secret values. |
| User | Day-to-day vault use: open assigned vaults, add entries to accessible vaults, and reveal/copy secrets when authorized by vault membership. |

Multi-role assignment is supported. Effective access is additive except that auditor-only accounts remain read-only for vault contents.

## Security and recovery model

Arkanum uses a trusted-server model:

1. SQL Server stores users, settings, audit events, vault metadata, and encrypted vault item content.
2. The Arkanum application server holds the runtime configuration and site recovery/master key inside the trusted boundary.
3. Authorized reveal/copy actions decrypt protected values server-side and write audit events.
4. Plaintext secrets should never be logged, exported in install reports, or committed to source control.

Audit rows are application append-only and chained with SHA-256 integrity hashes. Production SQL Server deployments should add a database deny UPDATE/DELETE control for the runtime account on `AuditEvents`, then capture retention, archive/export/restore, and forwarding evidence in [`docs/audit-compliance-evidence.md`](docs/audit-compliance-evidence.md).

Installer/runtime hardening separates the setup/migration identity from the least-privilege runtime SQL login, stores runtime secrets in protected files, and redacts installer output. See [`docs/installer-runtime-hardening.md`](docs/installer-runtime-hardening.md).

RMF/STIG review support is documented as a deployment-boundary evidence package, not a product-level approval claim. Use [`docs/rmf-stig-evidence-matrix.md`](docs/rmf-stig-evidence-matrix.md) for the implemented/partial/inherited/planned/not-applicable control map and [`docs/offline-release-evidence-bundle.md`](docs/offline-release-evidence-bundle.md) for the review-ready offline release evidence bundle.

### Site-wide recovery

Arkanum recovery is intentionally site-wide, not vault-specific:

```text
Recoverable = SQL Server backup + matching Arkanum recovery/master key
Not recoverable = SQL Server backup alone
```

If the SQL database is restored without the matching recovery/master key, existing encrypted credentials and secure notes cannot be decrypted by design.

See [`docs/backup-and-restore.md`](docs/backup-and-restore.md) for the restore sequence and backup materials.

## Installation targets

The intended feedback/deployment tracks are:

1. **Windows Server 2022 / IIS / SQL Server**
   - Primary enterprise feedback path.
   - Uses a published Arkanum web package, IIS, a dedicated app pool identity, and SQL Server or SQL Server Express.

2. **Linux / systemd / SQL Server**
   - Secondary supported path for Linux server teams.
   - Uses a published Arkanum web package, a locked-down `arkanum` service account, systemd, and an external or local SQL Server endpoint.

3. **Containerized SQL or app hosting**
   - Lab convenience only.
   - Not the only supported install path because many controlled networks restrict containers.

See:

- [`docs/1.1.0-release.md`](docs/1.1.0-release.md)
- [`docs/windows-server-2022-setup.md`](docs/windows-server-2022-setup.md)
- [`docs/1.0.0-release.md`](docs/1.0.0-release.md)
- [`docs/2026.1.2-release.md`](docs/2026.1.2-release.md)
- [`docs/2026.1.1-release.md`](docs/2026.1.1-release.md)
- [`docs/upgrade-and-native-packaging.md`](docs/upgrade-and-native-packaging.md)
- [`docs/phase1-installable-feedback-release.md`](docs/phase1-installable-feedback-release.md)
- [`docs/offline-package-checklist.md`](docs/offline-package-checklist.md)
- [`installers/windows/`](installers/windows/)
- [`installers/linux/`](installers/linux/)

Release packages use a full/update model with matching Windows and Linux naming:

```text
Arkanum-1.1.0-full-windows-x64.zip
Arkanum-1.1.0-update-windows-x64.zip
Arkanum-1.1.0-full-linux-x64.tar.gz
Arkanum-1.1.0-update-linux-x64.tar.gz
```

Use full packages for first install, side-by-side testing, and restore workflows. Use update packages only against an existing installation; update mode preserves configuration, Recovery Key/master-key material, database connectivity settings, and service binding settings while creating a pre-upgrade backup/report.

## Versioning

The current source version is **1.1.0**, adding encrypted credential history and configurable user feedback to the feature-based **MAJOR.MINOR.PATCH** family. There is no annual increment. The installers recognize legacy **2026.1.2 -> 1.0.0 or later** as an upgrade while rejecting real downgrades and unknown source families. Same-version repair preserves state. Older tags/releases/assets remain immutable, and their security evidence is not relabeled as evidence for this version. See [`docs/versioning.md`](docs/versioning.md) for supported paths, native package constraints, rollback and outstanding acceptance gates, and [`docs/1.1.0-release.md`](docs/1.1.0-release.md) for feature setup. Source preparation does not itself publish a release or establish native Windows acceptance.

## Local development

### Requirements

- .NET SDK 10.0.402 (pinned in `global.json`; .NET 10 LTS)
- SQL Server for durable/review deployments
- Optional: local SQLite fallback for lightweight review/dev flows

### Restore, build, and test

```bash
dotnet restore
dotnet build
dotnet test
```

The current test suite covers domain behavior, vault workflows, settings, login separation, role boundaries, health/install evidence, security hardening, and site recovery-key behavior.

### Run locally

For local review without a configured SQL Server connection string, Arkanum can use the SQLite fallback configured in `appsettings.json`.

```bash
export ARKANUM_MASTER_KEY="replace-with-local-dev-key-material"
dotnet run --project Arkanum.Web --urls http://127.0.0.1:5068
```

For SQL-backed review, provide a SQL Server connection string through environment configuration instead of committing secrets:

```bash
export ARKANUM_MASTER_KEY="replace-with-site-recovery-key"
export ConnectionStrings__DefaultConnection="Server=tcp:sql01,1433;Database=Arkanum;User Id=arkanum_app;Password=...;Encrypt=True;TrustServerCertificate=False;"
dotnet run --project Arkanum.Web --urls https://127.0.0.1:5068
```

Do not commit real SQL passwords, LDAP bind passwords, recovery keys, databases, runtime config, or local publish output.

## Health and install report

Arkanum exposes non-secret operational endpoints:

- `GET /health` (anonymous liveness only)
- `GET /health/details` (authenticated Administrator/Auditor readiness)
- `GET /install-report`

`/health` returns HTTP 200 with status `Alive` when the web process responds; it does not establish database readiness or Recovery Key compatibility. `/health/details` checks database reachability, required settings, encryption-key availability, and recovery-key/database compatibility, returning HTTP 200 when healthy and HTTP 503 when unhealthy. These endpoints intentionally avoid returning connection strings, passwords, filesystem paths, stack traces, or plaintext secrets.

`/install-report` summarizes health, counts, operational settings, federation readiness, and security-control evidence without exposing protected values.

## Federation and controlled-network evidence

Arkanum includes first-pass OIDC support and SAML readiness planning. Federated identity is intended to map an external assertion to an existing active non-local Arkanum user. Arkanum does not automatically promote roles from IdP claims by default, and federated assertions cannot impersonate local emergency administrators.

See [`docs/phase4-stig-saml-oidc.md`](docs/phase4-stig-saml-oidc.md) for the current evidence map and federation direction.

## Repository hygiene

The repository intentionally excludes runtime and local-only artifacts such as:

- `bin/` and `obj/`
- `publish/`
- `data/`
- local databases
- local recovery/master keys
- `.env` files
- generated release artifacts
- lab Keycloak realm exports containing demo credentials

Review `.gitignore` before adding new operational scripts or lab files.

## Documentation map

| Document | Purpose |
| --- | --- |
| [`docs/1.0.0-release.md`](docs/1.0.0-release.md) | SemVer transition, preservation tests and remaining native acceptance boundaries. |
| [`docs/2026.1.0-release.md`](docs/2026.1.0-release.md) | First production release notes, installer restore mode, vault UX, reminders, CSV export, and full/update packages. |
| [`docs/2026.1.1-release.md`](docs/2026.1.1-release.md) | Setup layout and wording polish; vault access moved below entry forms. |
| [`docs/backup-and-restore.md`](docs/backup-and-restore.md) | SQL database + recovery/master-key restore model. |
| [`docs/offline-package-checklist.md`](docs/offline-package-checklist.md) | Checklist for moving packages into controlled/disconnected networks. |
| [`docs/offline-release-evidence-bundle.md`](docs/offline-release-evidence-bundle.md) | RMF/ATO review-ready offline evidence bundle contents and air-gapped update process. |
| [`docs/rmf-stig-evidence-matrix.md`](docs/rmf-stig-evidence-matrix.md) | DoD/RMF/STIG deployment-boundary evidence matrix and inherited-control map. |
| [`docs/phase1-installable-feedback-release.md`](docs/phase1-installable-feedback-release.md) | Installable feedback release goals and boundaries. |
| [`docs/phase2-security-hardening.md`](docs/phase2-security-hardening.md) | Browser/security-header and error-handling hardening notes. |
| [`docs/phase3-role-separation.md`](docs/phase3-role-separation.md) | Role model and least-privilege behavior. |
| [`docs/phase3-release-evidence.md`](docs/phase3-release-evidence.md) | Release evidence and non-secret install-report notes. |
| [`docs/phase4-stig-saml-oidc.md`](docs/phase4-stig-saml-oidc.md) | Control mapping, OIDC/SAML direction, and smart-card stance. |
| [`docs/initial-sql-setup-notes.md`](docs/initial-sql-setup-notes.md) | SQL setup notes for review environments. |

## Status

Arkanum 1.0.0 starts the feature-based version family; 2026.1.0 remains the historical first production release line. Operators must still complete organization-specific security review, backup/restore validation, native platform acceptance, SQL Server/IIS hardening, and identity-provider validation before storing operational credentials. Existing 2026.1.2 security assessment material does not assess the new release.
