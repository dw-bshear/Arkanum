# Arkanum backup and restore guide

Arkanum backups must preserve three things together:

1. The SQL Server database.
2. The Arkanum master encryption key / Recovery Key.
3. Runtime configuration needed to reconnect the app to the database and key.

If the database is restored without the matching master key, stored vault secrets cannot be decrypted.

## Minimum materials needed to recover an Arkanum instance

Recoverable state requires all of the following:

1. SQL database backup or DBA-restored Arkanum database.
2. The matching Arkanum Recovery Key for that database.
3. Runtime configuration and SQL connection details for the restored environment.

The SQL database alone is not enough. The matching Arkanum Recovery Key is required to validate and decrypt the restored database sentinel and existing vault content.

## Backup contents

### SQL Server database

Back up the Arkanum database using the site's approved SQL Server backup process. The backup should include all application tables, audit events, users, vault metadata, encrypted vault items, and portal settings.

### Master encryption key / Recovery Key

Back up the master key from the platform-specific secret location.

Linux default location:

```text
/etc/arkanum/master-key
```

Windows default location:

```text
C:\ProgramData\Arkanum\secrets\master-key.txt
```

Protect this backup as highly sensitive. Anyone with both the database and matching Recovery Key may be able to decrypt vault secrets through a restored Arkanum instance.

### Runtime configuration

Back up the non-source runtime configuration directory.

Linux default:

```text
/etc/arkanum/
```

Windows default:

```text
C:\ProgramData\Arkanum\config\
```

Do not publish runtime configuration or secrets into release packages.

## Restore paths

Currently supported:

```text
Use database already restored by DBA
```

Planned/future restore paths:

```text
Restore from SQL backup file
Attach existing database files / manual DBA handoff
```

The installer exposes the future restore paths so the workflow is clear, but this release does not automate SQL `.bak` restore or MDF/LDF attach. For this release, restore the SQL database first using the site's SQL Server process, then run Arkanum restore mode against that database.

## Installer restore mode

Restore mode is separate from fresh install and upgrade.

Linux CLI example:

```bash
sudo env \
  ACTION=restore \
  RESTORE_SOURCE=dba-restored-database \
  ARKANUM_SQL_CONNECTION='Server=tcp:<host>,1433;Database=Arkanum;User Id=<runtime-user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;' \
  ARKANUM_RESTORE_KEY_FILE='/path/to/master-key.txt' \
  installers/linux/install-arkanum.sh
```

Windows CLI example:

```powershell
.\installers\windows\Install-Arkanum.ps1 `
  -Action restore `
  -RestoreSource dba-restored-database `
  -SqlConnectionString 'Server=tcp:<host>,1433;Database=Arkanum;User Id=<runtime-user>;Password=<password>;Encrypt=True;TrustServerCertificate=True;' `
  -RestoreKeyFile 'C:\SecureBackup\master-key.txt'
```

The installer does not generate a new Recovery Key in restore mode. It installs the provided key, starts the app, runs the health/sentinel validation, and writes a restore report to the standard install log/report location.

Restore reports include the restore source, Recovery Key source type, health result, and Recovery sentinel validation. They must not include SQL passwords or Recovery Key values.

## Restore sequence

1. Stop Arkanum.
2. Restore the SQL Server database using the site's approved DBA process.
3. Run Arkanum installer restore mode with `RESTORE_SOURCE=dba-restored-database` / `-RestoreSource dba-restored-database`.
4. Provide the matching Recovery Key by file or pasted value.
5. Provide the runtime SQL connection for the restored database.
6. Let the installer start Arkanum and validate `/health`.
7. Review the restore report.
8. Log in as an administrator and verify vault metadata and audit visibility.
9. Test reveal/copy on a known non-production test credential if available.

## Recovery Key mismatch behavior

If the provided key does not match the restored database, restore mode fails closed with this message:

```text
The provided Arkanum Recovery Key does not match the restored database.
```

The installer also explains that recoverable state requires both the SQL database backup and the matching Arkanum Recovery Key. No new key is generated during restore mode.

## Health check

After restore, validate:

```bash
curl -fsS http://127.0.0.1:5068/health
```

A healthy restore returns HTTP 200 and `status: Healthy`. An unhealthy restore returns HTTP 503 and component statuses that do not expose secrets.

## Backup validation cadence

For feedback deployments, validate restore at least once before using Arkanum for real operational credentials. For production-like deployments, schedule periodic restore tests according to the local system owner's continuity requirements.
