# Offline package checklist

Use this checklist before moving an Conclave release package into a disconnected or controlled network.

## Release package contents

- Published Conclave web application files.
- Windows installer scripts under `installers/windows/`.
- Linux installer scripts under `installers/linux/`.
- SQL setup/migration scripts, when available.
- Installation and backup documentation under `docs/`.
- Release notes.
- SHA-256 checksums.
- SBOM, when release automation provides one.

## Pre-transfer checks

- Confirm target OS: Windows Server 2022 or supported Linux distribution.
- Confirm ASP.NET Core runtime/hosting bundle availability.
- Confirm SQL Server version and connectivity plan.
- Confirm service account/app pool identity plan.
- Confirm DNS/hostname and port/TLS plan.
- Confirm initial local administrator owner.
- Confirm backup location for SQL database and master encryption key.
- Confirm whether LDAP/AD integration will be configured during or after initial install.

## Integrity checks

Generate checksums before transfer:

```bash
sha256sum Conclave-*.zip Conclave-*.tar.gz > SHA256SUMS.txt
```

Verify checksums after transfer using the receiving network's approved tool.

## Secrets handling

Do not include these values in the release package:

- SQL passwords.
- LDAP bind passwords.
- Conclave master encryption key.
- Initial administrator password.
- Site-specific TLS private keys.

Create or import secrets during installation on the target network.

## Post-install checks

- Installer completed without errors.
- `/health` returns HTTP 200 and `Healthy`.
- First administrator can sign in.
- Admin can view Settings.
- Admin can create/import users as appropriate.
- A test vault credential and secure note can be saved and revealed.
- Audit events are visible for reveal/copy activity.
- Backup procedure has been reviewed and tested with non-production data.

## Language guidance

Until formal evidence exists, describe Conclave as:

> Designed for controlled and disconnected networks.

Avoid unsupported claims such as:

- STIG compliant.
- FIPS certified.
- DISA approved.
- FedRAMP ready.
- Military-grade encryption.
