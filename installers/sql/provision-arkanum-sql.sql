/*
Arkanum SQL provisioning script.
Run with a setup/admin SQL credential only during installation.
Do not persist the setup/admin credential; persist only the runtime SQL connection string.
Run arkanum-schema.sql with a setup/migration identity before granting runtime permissions.

Expected sqlcmd variables:
  DatabaseName
  RuntimeLogin
  RuntimePassword
*/

:setvar DatabaseName "Arkanum"
:setvar RuntimeLogin "arkanum_app"
:setvar RuntimePassword "ChangeMeBeforeProduction!"

USE [master];
GO

IF DB_ID(N'$(DatabaseName)') IS NULL
BEGIN
    DECLARE @createDatabaseSql nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(N'$(DatabaseName)') + N';';
    EXEC (@createDatabaseSql);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'$(RuntimeLogin)')
BEGIN
    DECLARE @createLoginSql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(N'$(RuntimeLogin)') + N' WITH PASSWORD = ' + QUOTENAME(N'$(RuntimePassword)', '''') + N', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;';
    EXEC (@createLoginSql);
END
GO

DECLARE @useDatabaseSql nvarchar(max) = N'
USE ' + QUOTENAME(N'$(DatabaseName)') + N';
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N''$(RuntimeLogin)'')
BEGIN
    CREATE USER ' + QUOTENAME(N'$(RuntimeLogin)') + N' FOR LOGIN ' + QUOTENAME(N'$(RuntimeLogin)') + N';
END;
PRINT ''Runtime least privilege: table-level DML grants only; no broad built-in database role membership.'';
GRANT SELECT ON SCHEMA::[dbo] TO ' + QUOTENAME(N'$(RuntimeLogin)') + N';
GRANT INSERT ON SCHEMA::[dbo] TO ' + QUOTENAME(N'$(RuntimeLogin)') + N';
GRANT UPDATE ON SCHEMA::[dbo] TO ' + QUOTENAME(N'$(RuntimeLogin)') + N';
GRANT DELETE ON SCHEMA::[dbo] TO ' + QUOTENAME(N'$(RuntimeLogin)') + N';
DENY UPDATE ON OBJECT::[dbo].[AuditEvents] TO ' + QUOTENAME(N'$(RuntimeLogin)') + N';
DENY DELETE ON OBJECT::[dbo].[AuditEvents] TO ' + QUOTENAME(N'$(RuntimeLogin)') + N';
';
EXEC (@useDatabaseSql);
GO

PRINT 'Arkanum SQL provisioning completed: database and least-privilege runtime login/user are ready.';
