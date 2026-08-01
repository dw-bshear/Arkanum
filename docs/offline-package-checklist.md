# Offline package checklist

Use this checklist before moving an Arkanum release package into a disconnected or controlled network.

## Release package contents

- Published Arkanum web application files.
- Windows installer scripts under `installers/windows/`.
- Linux installer scripts under `installers/linux/`.
- SQL setup/migration scripts, when available.
- Installation and backup documentation under `docs/`.
- Release notes.
- `packages.lock.json` files for every restored .NET project.
- SHA-256 checksums.
- SBOM in CycloneDX or SPDX format.
- Vulnerability scan evidence for the exact release commit.

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

Use locked restore and vulnerability evidence before packaging:

```bash
dotnet restore --locked-mode Arkanum.sln
dotnet list Arkanum.sln package --vulnerable --include-transitive
dotnet list Arkanum.sln package --outdated --include-transitive
```

For a disconnected build, pre-stage an approved offline NuGet cache or feed containing exactly the packages resolved in each `packages.lock.json`. Do not restore from the public internet inside the receiving network.

Generate an SBOM and checksums before transfer:

```bash
# Use the release pipeline's approved CycloneDX or SPDX generator.
# Example shape: dotnet CycloneDX Arkanum.sln --output artifacts/sbom
sha256sum Arkanum-*.zip Arkanum-*.tar.gz artifacts/sbom/* > SHA256SUMS.txt
```

Verify checksums after transfer using the receiving network's approved tool.

## Secrets handling

Do not include these values in the release package:

- SQL passwords.
- LDAP bind passwords.
- Arkanum master encryption key.
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

Until formal evidence exists, describe Arkanum as:

> Designed for controlled and disconnected networks.

Avoid unsupported claims such as:

- STIG compliant.
- FIPS certified.
- DISA approved.
- FedRAMP ready.
- Military-grade encryption.
