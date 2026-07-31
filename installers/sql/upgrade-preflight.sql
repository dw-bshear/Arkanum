/*
Arkanum SQL upgrade preflight script.
Run with the limited runtime SQL connection before upgrade/migration.
This script reports database reachability, schema/version markers, and runtime login permissions.
*/

SELECT DB_NAME();
SELECT 'Database reachability preflight' AS CheckName, DB_NAME() AS DatabaseName, SUSER_SNAME() AS LoginName, USER_NAME() AS DatabaseUser;

IF OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL
BEGIN
    SELECT 'Schema/version preflight' AS CheckName, 'Missing __EFMigrationsHistory table; schema mismatch or database not initialized.' AS Result;
END
ELSE
BEGIN
    SELECT TOP (5) 'Schema/version preflight' AS CheckName, [MigrationId], [ProductVersion]
    FROM [__EFMigrationsHistory]
    ORDER BY [MigrationId] DESC;
END

SELECT 'Runtime login permission preflight' AS CheckName, 'SELECT' AS PermissionName, HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'SELECT') AS HasPermission;
SELECT 'Runtime login permission preflight' AS CheckName, 'INSERT' AS PermissionName, HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'INSERT') AS HasPermission;
SELECT 'Runtime login permission preflight' AS CheckName, 'UPDATE' AS PermissionName, HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'UPDATE') AS HasPermission;
SELECT 'Runtime login permission preflight' AS CheckName, 'DELETE' AS PermissionName, HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'DELETE') AS HasPermission;
SELECT 'Runtime login permission preflight' AS CheckName, 'ALTER' AS PermissionName, HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') AS HasPermission;

PRINT 'SQL backup strongly recommended before upgrade. Keep a restorable SQL backup plus matching Arkanum Recovery Key before applying migrations.';
PRINT 'Failure categories: database reachability, schema mismatch, permission problem, recovery-key mismatch.';
