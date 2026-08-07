using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Data;

public sealed class ArkanumDbContext(DbContextOptions<ArkanumDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> Users => Set<AppUserEntity>();
    public DbSet<AuditEventEntity> AuditEvents => Set<AuditEventEntity>();
    public DbSet<VaultEntity> Vaults => Set<VaultEntity>();
    public DbSet<VaultFolderEntity> VaultFolders => Set<VaultFolderEntity>();
    public DbSet<VaultItemEntity> VaultItems => Set<VaultItemEntity>();
    public DbSet<VaultMemberEntity> VaultMembers => Set<VaultMemberEntity>();
    public DbSet<DashboardWidgetEntity> DashboardWidgets => Set<DashboardWidgetEntity>();
    public DbSet<PortalSettingEntity> PortalSettings => Set<PortalSettingEntity>();
    public DbSet<RecoveryStateEntity> RecoveryStates => Set<RecoveryStateEntity>();
    public DbSet<UserGroupEntity> UserGroups => Set<UserGroupEntity>();
    public DbSet<UserGroupMemberEntity> UserGroupMembers => Set<UserGroupMemberEntity>();
    public DbSet<UserGroupManagerEntity> UserGroupManagers => Set<UserGroupManagerEntity>();

    public override int SaveChanges()
    {
        PrepareAuditEventsForSave();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PrepareAuditEventsForSave();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUserEntity>().HasKey(u => u.UserKey);
        modelBuilder.Entity<AppUserEntity>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<AppUserEntity>().Property(u => u.RolesCsv).HasMaxLength(256);
        modelBuilder.Entity<AuditEventEntity>().HasKey(e => e.Id);
        modelBuilder.Entity<AuditEventEntity>().HasIndex(e => e.Sequence).IsUnique();
        modelBuilder.Entity<VaultEntity>().HasKey(v => v.Id);
        modelBuilder.Entity<VaultFolderEntity>().HasKey(f => f.Id);
        modelBuilder.Entity<VaultFolderEntity>().HasIndex(f => new { f.VaultId, f.Name });
        modelBuilder.Entity<VaultItemEntity>().HasKey(i => i.Id);
        modelBuilder.Entity<VaultMemberEntity>().HasKey(m => new { m.VaultId, m.UserKey });
        modelBuilder.Entity<VaultMemberEntity>().HasIndex(m => m.UserKey);
        modelBuilder.Entity<DashboardWidgetEntity>().HasKey(w => w.WidgetKey);
        modelBuilder.Entity<PortalSettingEntity>().HasKey(s => s.SettingKey);
        modelBuilder.Entity<RecoveryStateEntity>().HasKey(s => s.Id);
        modelBuilder.Entity<UserGroupEntity>().HasKey(g => g.Id);
        modelBuilder.Entity<UserGroupMemberEntity>().HasKey(m => new { m.GroupId, m.UserKey });
        modelBuilder.Entity<UserGroupManagerEntity>().HasKey(m => new { m.GroupId, m.UserKey });
    }

    private void PrepareAuditEventsForSave()
    {
        var auditEntries = ChangeTracker.Entries<AuditEventEntity>().ToArray();
        var unsafeEntries = auditEntries.Where(entry => entry.State is EntityState.Modified or EntityState.Deleted).ToArray();
        if (unsafeEntries.Length > 0)
        {
            throw new InvalidOperationException("Audit events are append-only. Update and delete operations are blocked by the application layer; production database accounts should also deny UPDATE and DELETE on AuditEvents.");
        }

        var added = auditEntries
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .OrderBy(evt => evt.OccurredAt)
            .ThenBy(evt => evt.Id, StringComparer.Ordinal)
            .ToArray();
        if (added.Length == 0) return;

        var persisted = Set<AuditEventEntity>()
            .AsNoTracking()
            .OrderByDescending(evt => evt.Sequence)
            .FirstOrDefault();
        var sequence = persisted?.Sequence ?? 0;
        var previousHash = persisted?.IntegrityHash ?? string.Empty;
        foreach (var auditEvent in added)
        {
            sequence++;
            AuditEventIntegrity.Stamp(auditEvent, sequence, previousHash);
            previousHash = auditEvent.IntegrityHash;
        }
    }
}

public sealed class AppUserEntity
{
    public string UserKey { get; set; } = Guid.NewGuid().ToString("n");
    public string? ExternalId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public UserSource Source { get; set; }
    public bool DirectoryPresent { get; set; }
    public bool AppAccessEnabled { get; set; }
    public string DisabledReason { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string RolesCsv { get; set; } = string.Empty;
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockoutEndAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AuditEventEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public AuditEventType Type { get; set; }
    public string ActorUserKey { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string PreviousIntegrityHash { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;
    public string IntegrityAlgorithm { get; set; } = AuditEventIntegrity.Algorithm;
}

public sealed class VaultEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CreatedByUserKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsRetired { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public string RetiredByUserKey { get; set; } = string.Empty;
    public string RetirementReason { get; set; } = string.Empty;
}

public sealed class VaultFolderEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VaultId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class VaultItemEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VaultId { get; set; }
    public Guid? FolderId { get; set; }
    public VaultItemType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string EncryptedSecret { get; set; } = string.Empty;
    public string EncryptionKeyId { get; set; } = "env:v1";
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public DateTimeOffset EncryptedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateOnly? ResetNotificationDate { get; set; }
    public DateTimeOffset? ResetNotificationSentAt { get; set; }
}

public sealed class VaultMemberEntity
{
    public Guid VaultId { get; set; }
    public string UserKey { get; set; } = string.Empty;
    public bool CanInvite { get; set; }
    public string AddedByUserKey { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserGroupEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RecoveryStateEntity
{
    public string Id { get; set; } = SiteRecoveryKeyService.SingletonStateId;
    public string RecoveryKeyId { get; set; } = "env:v1";
    public string EncryptedSentinel { get; set; } = string.Empty;
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserGroupMemberEntity
{
    public Guid GroupId { get; set; }
    public string UserKey { get; set; } = string.Empty;
    public string AddedByUserKey { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserGroupManagerEntity
{
    public Guid GroupId { get; set; }
    public string UserKey { get; set; } = string.Empty;
    public string AddedByUserKey { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
