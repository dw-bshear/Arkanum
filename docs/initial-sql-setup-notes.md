# Arkanum initial SQL Server setup notes

The installer now treats SQL Server setup as an explicit installation workflow instead of requiring the operator to pre-create a final application connection string by hand.

## Database setup mode

Supported database setup modes:

```text
Auto detect
Use existing database
Create new database
Restore from database backup
```

For `Use existing database`, provide an existing runtime SQL connection string or runtime SQL host/database/login/password values.

For `Create new database`, the installer collects:

```text
SQL Server host
SQL Server port
Setup/admin SQL username
Setup/admin SQL password
Database name
Runtime app login name
Runtime app login password or Generate runtime password
Encrypt connection
Trust server certificate
```

The setup/admin SQL credential is used only during installation/provisioning. It is not written to the install-state manifest, runtime config, app environment file, web.config, or install report.

Persist only the runtime SQL connection after provisioning. The runtime connection uses the limited runtime login.

## Limited runtime login

The provisioning script creates or reuses:

- the Arkanum database;
- a SQL login for the runtime application account;
- a database user mapped to that login;
- database-level membership needed for Arkanum runtime and EF migrations.

The runtime login is intentionally database-scoped. It is not granted server-wide roles such as sysadmin or serveradmin.

Current role grants:

```text
db_datareader
db_datawriter
db_ddladmin
```

`db_ddladmin` is included so EF Core schema updates can run during controlled installer upgrade/migration flows. If a future release moves all schema changes to a separate setup account, this can be reduced further.

## SQL upgrade preflight

Upgrade mode performs SQL upgrade preflight before replacing/restarting application files.

Preflight checks include:

- database reachability;
- current database/schema marker check using `__EFMigrationsHistory`;
- runtime login permission preflight;
- clear failure categories for database reachability, schema mismatch, permission problem, and recovery-key mismatch;
- pre-upgrade backup location/report guidance.

A pre-upgrade backup is strongly recommended before migrations or file changes. Installer upgrade reports include rollback guidance for application files; database rollback remains a DBA operation requiring the matching SQL backup and Arkanum Recovery Key.

## Reports and secret handling

Reports must not include SQL passwords, setup credentials, runtime passwords, or Recovery Keys.

Reports may include non-secret metadata such as:

- database setup mode;
- database name;
- runtime SQL login name;
- report path;
- state manifest path;
- pre-upgrade backup path;
- health/preflight status.

## Operator notes

The admin Settings page can store SQL Server connection values for display/configuration, but those values do not by themselves move a running Arkanum instance to a different database. The active database provider is selected at application startup from `ConnectionStrings:DefaultConnection`.

Initial database creation, runtime login provisioning, restore, and upgrade/migration should remain dedicated setup/install steps rather than normal in-app save actions.
