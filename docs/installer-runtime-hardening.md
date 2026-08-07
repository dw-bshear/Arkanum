# Installer/runtime hardening

Arkanum production installs separate setup/migration duties from the least-privilege runtime identity.

## Database lifecycle

Use a setup/migration identity only during installation or upgrade to create the database and run `installers/sql/arkanum-schema.sql`. The runtime login is created or mapped afterward by `installers/sql/provision-arkanum-sql.sql` and receives table-level DML grants only. It is not a schema owner and is not a member of broad roles such as `db_ddladmin`, `db_datawriter`, or `db_datareader`.

The application no longer relies on startup raw schema patches for SQL Server production. Runtime schema management is disabled by default and can only be enabled with `Arkanum:AllowRuntimeSchemaManagement` for controlled maintenance/lab scenarios. Testing/InMemory startup is isolated from relational SQL execution.

## protected configuration

Runtime secrets are stored in protected files with restrictive ACLs:

- site recovery/master key file
- runtime SQL connection string file

Installers pass only file paths to the app with `ARKANUM_MASTER_KEY_FILE` and `ConnectionStrings__DefaultConnection_FILE`. Windows uses ACLs for Administrators, SYSTEM, and the IIS app pool identity. Linux writes root-owned files readable only by the service group.

## Log and transcript handling

Installer output must redact setup SQL passwords, runtime SQL passwords, full credentialed connection strings, pasted Recovery Keys, and other secret material. Reports should state the source or path category without writing secret values.

## Production configuration

Production packages should provide explicit SQL Server configuration and protected secret files. Development SQLite fallback paths should be relative/local only and must not point to a maintainer workstation or lab path.
