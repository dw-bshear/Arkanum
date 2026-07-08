# Conclave 2026.0.1 release notes

Conclave 2026.0.1 is a frozen internal-use fork derived from Arkanum 2026.0.6.

This release is intended as a one-time deliverable for a larger testing partner. Ongoing product development remains on Arkanum, not Conclave.

## Product boundary

```text
Product: Conclave
Version: 2026.0.1
Derived from: Arkanum 2026.0.6
Branch: conclave
Status: frozen internal-use fork
```

Recommended usage terms should clarify that Conclave does not include automatic rights to future Arkanum versions, updates, roadmap items, branding, support, redistribution, resale, or public hosting unless separately agreed in writing.

## Included capabilities

- Vaults, folders, login credentials, and secure notes.
- Collapsed-by-default vault trees so entry names are not exposed until a user expands the relevant folder/subtree.
- Separate Credentials and Secure Notes subtrees inside each vault folder, with alphabetized entries.
- Inline reveal/copy with masked placeholders.
- Per-entry reset notification dates.
- Encrypted CSV export package containing `manifest.json` and `credentials.csv.aes`.
- Installer restore mode with Recovery Key file/paste input.
- SQL Server-backed deployment support.
- Windows and Linux installer/package tracks.

## Installer restore mode

Conclave restore mode requires both:

```text
SQL database backup/restored database
matching Conclave Recovery Key
```

If validation fails, restore mode reports:

```text
The provided Recovery Key does not match this database.
```

## Expected package assets

```text
Conclave-2026.0.1-windows-x64.zip
Conclave-2026.0.1-windows-x64.zip.sha256
Conclave-2026.0.1-linux-x64.tar.gz
Conclave-2026.0.1-linux-x64.tar.gz.sha256
Conclave-2026.0.1-linux-x64.deb
Conclave-2026.0.1-linux-x64.deb.sha256
```

## Notes

Internal .NET project names may still reference the original source lineage. Public-facing package names, installer defaults, documentation, and UI branding identify this fork as Conclave.
