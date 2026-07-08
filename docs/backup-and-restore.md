# Conclave backup and restore guide

Conclave backups must preserve three things together:

1. The SQL Server database.
2. The Conclave master encryption key.
3. Runtime configuration needed to reconnect the app to the database and key.

If the database is restored without the matching master key, stored vault secrets cannot be decrypted.

## Backup contents

### SQL Server database

Back up the Conclave database using the site's approved SQL Server backup process. The backup should include all application tables, audit events, users, vault metadata, encrypted vault items, and portal settings.

### Master encryption key

Back up the master key from the platform-specific secret location.

Linux default Phase 1 location:

```text
/etc/conclave/master-key
```

Windows default Phase 1 location:

```text
C:\ProgramData\Conclave\secrets\master-key.txt
```

Protect this backup as highly sensitive. Anyone with both the database and master key may be able to decrypt vault secrets through a restored Conclave instance.

### Runtime configuration

Back up the non-source runtime configuration directory.

Linux default:

```text
/etc/conclave/
```

Windows default:

```text
C:\ProgramData\Conclave\config\
```

Do not publish runtime configuration or secrets into release packages.

## Restore sequence

1. Stop Conclave.
2. Restore the SQL Server database.
3. Restore the master encryption key with the same file permissions/ACLs.
4. Restore runtime configuration.
5. Start Conclave.
6. Run the installation health check.
7. Log in as an administrator and verify vault metadata and audit visibility.
8. Test reveal/copy on a known non-production test credential if available.

## Health check

After restore, validate:

```bash
curl -fsS http://127.0.0.1:5068/health
```

A healthy restore returns HTTP 200 and `status: Healthy`. An unhealthy restore returns HTTP 503 and component statuses that do not expose secrets.

## Backup validation cadence

For feedback deployments, validate restore at least once before using Conclave for real operational credentials. For production-like deployments, schedule periodic restore tests according to the local system owner's continuity requirements.
