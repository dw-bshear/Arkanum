/*
Arkanum controlled schema script.
Run this with a setup/migration identity during install or upgrade before granting/revalidating the runtime login.
*/

:setvar DatabaseName "Arkanum"

USE [$(DatabaseName)];
GO

IF OBJECT_ID(N'Users', N'U') IS NULL
CREATE TABLE [Users] (
    [UserKey] nvarchar(64) NOT NULL CONSTRAINT [PK_Users] PRIMARY KEY,
    [ExternalId] nvarchar(256) NULL,
    [Username] nvarchar(256) NOT NULL,
    [DisplayName] nvarchar(256) NOT NULL,
    [Email] nvarchar(256) NOT NULL,
    [Source] int NOT NULL,
    [DirectoryPresent] bit NOT NULL,
    [AppAccessEnabled] bit NOT NULL,
    [DisabledReason] nvarchar(max) NOT NULL,
    [PasswordHash] nvarchar(max) NOT NULL,
    [RolesCsv] nvarchar(256) NOT NULL,
    [FailedLoginCount] int NOT NULL CONSTRAINT [DF_Users_FailedLoginCount] DEFAULT 0,
    [LockoutEndAt] datetimeoffset NULL,
    [CreatedAt] datetimeoffset NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Users_Username' AND object_id = OBJECT_ID(N'Users')) CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
GO

IF OBJECT_ID(N'AuditEvents', N'U') IS NULL
CREATE TABLE [AuditEvents] (
    [Id] nvarchar(64) NOT NULL CONSTRAINT [PK_AuditEvents] PRIMARY KEY,
    [Type] int NOT NULL,
    [ActorUserKey] nvarchar(256) NOT NULL,
    [OccurredAt] datetimeoffset NOT NULL,
    [Target] nvarchar(max) NOT NULL,
    [Message] nvarchar(max) NOT NULL,
    [Sequence] bigint NOT NULL CONSTRAINT [DF_AuditEvents_Sequence] DEFAULT 0,
    [PreviousIntegrityHash] nvarchar(max) NOT NULL CONSTRAINT [DF_AuditEvents_PreviousIntegrityHash] DEFAULT '',
    [IntegrityHash] nvarchar(max) NOT NULL CONSTRAINT [DF_AuditEvents_IntegrityHash] DEFAULT '',
    [IntegrityAlgorithm] nvarchar(64) NOT NULL CONSTRAINT [DF_AuditEvents_IntegrityAlgorithm] DEFAULT 'SHA-256'
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditEvents_Sequence' AND object_id = OBJECT_ID(N'AuditEvents')) CREATE UNIQUE INDEX [IX_AuditEvents_Sequence] ON [AuditEvents] ([Sequence]);
GO

IF OBJECT_ID(N'Vaults', N'U') IS NULL
CREATE TABLE [Vaults] (
    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Vaults] PRIMARY KEY,
    [Name] nvarchar(256) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [CreatedByUserKey] nvarchar(256) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [IsRetired] bit NOT NULL CONSTRAINT [DF_Vaults_IsRetired] DEFAULT 0,
    [RetiredAt] datetimeoffset NULL,
    [RetiredByUserKey] nvarchar(max) NOT NULL CONSTRAINT [DF_Vaults_RetiredByUserKey] DEFAULT '',
    [RetirementReason] nvarchar(max) NOT NULL CONSTRAINT [DF_Vaults_RetirementReason] DEFAULT ''
);
GO

IF OBJECT_ID(N'VaultFolders', N'U') IS NULL
CREATE TABLE [VaultFolders] (
    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_VaultFolders] PRIMARY KEY,
    [VaultId] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VaultFolders_VaultId_Name' AND object_id = OBJECT_ID(N'VaultFolders')) CREATE INDEX [IX_VaultFolders_VaultId_Name] ON [VaultFolders] ([VaultId], [Name]);
GO

IF OBJECT_ID(N'VaultItems', N'U') IS NULL
CREATE TABLE [VaultItems] (
    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_VaultItems] PRIMARY KEY,
    [VaultId] uniqueidentifier NOT NULL,
    [FolderId] uniqueidentifier NULL,
    [Type] int NOT NULL,
    [Title] nvarchar(256) NOT NULL,
    [Username] nvarchar(256) NULL,
    [EncryptedSecret] nvarchar(max) NOT NULL,
    [EncryptionKeyId] nvarchar(128) NOT NULL,
    [EncryptionAlgorithm] nvarchar(64) NOT NULL,
    [EncryptedAt] datetimeoffset NOT NULL,
    [ResetNotificationDate] date NULL,
    [ResetNotificationSentAt] datetimeoffset NULL
);
GO

IF OBJECT_ID(N'CredentialVersions', N'U') IS NULL
CREATE TABLE [CredentialVersions] (
    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_CredentialVersions] PRIMARY KEY,
    [VaultId] uniqueidentifier NOT NULL,
    [ItemId] uniqueidentifier NOT NULL,
    [Revision] bigint NOT NULL,
    [SavedAt] datetimeoffset NOT NULL,
    [EncryptedPayload] nvarchar(max) NOT NULL,
    [EncryptionKeyId] nvarchar(128) NOT NULL,
    [EncryptionAlgorithm] nvarchar(64) NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CredentialVersions_ItemId_Revision' AND object_id = OBJECT_ID(N'CredentialVersions')) CREATE UNIQUE INDEX [IX_CredentialVersions_ItemId_Revision] ON [CredentialVersions] ([ItemId], [Revision]);
GO

IF OBJECT_ID(N'VaultMembers', N'U') IS NULL
CREATE TABLE [VaultMembers] (
    [VaultId] uniqueidentifier NOT NULL,
    [UserKey] nvarchar(64) NOT NULL,
    [CanInvite] bit NOT NULL,
    [AddedByUserKey] nvarchar(64) NOT NULL,
    [AddedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_VaultMembers] PRIMARY KEY ([VaultId], [UserKey])
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VaultMembers_UserKey' AND object_id = OBJECT_ID(N'VaultMembers')) CREATE INDEX [IX_VaultMembers_UserKey] ON [VaultMembers] ([UserKey]);
GO

IF OBJECT_ID(N'DashboardWidgets', N'U') IS NULL
CREATE TABLE [DashboardWidgets] (
    [WidgetKey] nvarchar(128) NOT NULL CONSTRAINT [PK_DashboardWidgets] PRIMARY KEY,
    [Title] nvarchar(256) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [Enabled] bit NOT NULL,
    [SortOrder] int NOT NULL,
    [RolesCsv] nvarchar(256) NOT NULL
);
GO

IF OBJECT_ID(N'PortalSettings', N'U') IS NULL
CREATE TABLE [PortalSettings] (
    [SettingKey] nvarchar(256) NOT NULL CONSTRAINT [PK_PortalSettings] PRIMARY KEY,
    [Value] nvarchar(max) NOT NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    [UpdatedByUserKey] nvarchar(256) NOT NULL
);
GO

IF OBJECT_ID(N'RecoveryStates', N'U') IS NULL
CREATE TABLE [RecoveryStates] (
    [Id] nvarchar(64) NOT NULL CONSTRAINT [PK_RecoveryStates] PRIMARY KEY,
    [RecoveryKeyId] nvarchar(128) NOT NULL,
    [EncryptedSentinel] nvarchar(max) NOT NULL,
    [EncryptionAlgorithm] nvarchar(64) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL
);
GO

IF OBJECT_ID(N'UserGroups', N'U') IS NULL
CREATE TABLE [UserGroups] (
    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_UserGroups] PRIMARY KEY,
    [Name] nvarchar(256) NOT NULL,
    [Description] nvarchar(max) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL
);
GO

IF OBJECT_ID(N'UserGroupMembers', N'U') IS NULL
CREATE TABLE [UserGroupMembers] (
    [GroupId] uniqueidentifier NOT NULL,
    [UserKey] nvarchar(64) NOT NULL,
    [AddedByUserKey] nvarchar(64) NOT NULL,
    [AddedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_UserGroupMembers] PRIMARY KEY ([GroupId], [UserKey])
);
GO

IF OBJECT_ID(N'UserGroupManagers', N'U') IS NULL
CREATE TABLE [UserGroupManagers] (
    [GroupId] uniqueidentifier NOT NULL,
    [UserKey] nvarchar(64) NOT NULL,
    [AddedByUserKey] nvarchar(64) NOT NULL,
    [AddedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_UserGroupManagers] PRIMARY KEY ([GroupId], [UserKey])
);
GO

PRINT 'Arkanum schema is present.';
