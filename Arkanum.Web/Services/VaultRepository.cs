using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class VaultRepository
{
    public async Task<VaultEntity> CreateVaultAsync(ArkanumDbContext db, string name, string description, string actorUserKey, DateTimeOffset now)
    {
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)) throw new ArgumentException("Vault name is required.", nameof(name));

        var vault = new VaultEntity
        {
            Name = trimmedName,
            Description = description.Trim(),
            CreatedByUserKey = actorUserKey,
            CreatedAt = now
        };
        db.Vaults.Add(vault);
        db.VaultMembers.Add(new VaultMemberEntity
        {
            VaultId = vault.Id,
            UserKey = actorUserKey,
            CanInvite = true,
            AddedByUserKey = actorUserKey,
            AddedAt = now
        });
        db.VaultFolders.Add(new VaultFolderEntity
        {
            VaultId = vault.Id,
            Name = "General",
            CreatedAt = now
        });
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.VaultAssignmentChanged,
            ActorUserKey = actorUserKey,
            Target = vault.Name,
            OccurredAt = now,
            Message = "Vault created with default General folder."
        });
        await db.SaveChangesAsync();
        return vault;
    }

    public async Task<VaultEntity> EnsurePersonalVaultAsync(ArkanumDbContext db, string userKey, string username, DateTimeOffset now)
    {
        var existing = await db.VaultMembers
            .Where(m => m.UserKey == userKey && m.CanInvite)
            .Join(db.Vaults, m => m.VaultId, v => v.Id, (_, v) => v)
            .SingleOrDefaultAsync(v => v.CreatedByUserKey == userKey && v.Name == PersonalVaultName(username));
        if (existing is not null) return existing;

        return await CreateVaultAsync(db, PersonalVaultName(username), "Your personal credential vault. Invite other users when you want to share access.", userKey, now);
    }

    public async Task<IReadOnlyList<VaultSummary>> ListVaultsAsync(ArkanumDbContext db)
    {
        var vaults = await db.Vaults.AsNoTracking().OrderBy(v => v.Name).ToListAsync();
        return await SummarizeVaultsAsync(db, vaults);
    }

    public async Task<IReadOnlyList<VaultSummary>> ListVaultsForUserAsync(ArkanumDbContext db, string userKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        if (ArkanumRoleCapabilities.CanReviewAllVaults(roles))
        {
            return await ListVaultsAsync(db);
        }

        var vaults = await db.VaultMembers.AsNoTracking()
            .Where(m => m.UserKey == userKey)
            .Join(db.Vaults.AsNoTracking(), m => m.VaultId, v => v.Id, (_, v) => v)
            .OrderBy(v => v.Name)
            .ToListAsync();
        return await SummarizeVaultsAsync(db, vaults);
    }

    public async Task<VaultDetails?> GetVaultDetailsAsync(ArkanumDbContext db, Guid vaultId)
    {
        var vault = await db.Vaults.AsNoTracking().SingleOrDefaultAsync(v => v.Id == vaultId);
        if (vault is null) return null;
        var folders = await db.VaultFolders.AsNoTracking().Where(f => f.VaultId == vaultId).OrderBy(f => f.Name).ToListAsync();
        var items = await db.VaultItems.AsNoTracking().Where(i => i.VaultId == vaultId).OrderBy(i => i.Title).ToListAsync();
        var memberRows = await db.VaultMembers.AsNoTracking()
            .Where(m => m.VaultId == vaultId)
            .Join(db.Users.AsNoTracking(), m => m.UserKey, u => u.UserKey, (m, u) => new { u.UserKey, u.Username, u.DisplayName, m.CanInvite })
            .OrderBy(m => m.Username)
            .ToListAsync();
        var members = memberRows.Select(m => new VaultMemberSummary(m.UserKey, m.Username, m.DisplayName, m.CanInvite)).ToArray();
        return new VaultDetails(
            vault.Id,
            vault.Name,
            vault.Description,
            members,
            folders.Select(f => new VaultFolderDetails(
                f.Id,
                f.Name,
                items.Where(i => i.FolderId == f.Id).Select(i => new VaultItemSummary(i.Id, i.Title, i.Type, i.Username, i.ResetNotificationDate)).ToArray())).ToArray());
    }

    public async Task<bool> CanAccessVaultAsync(ArkanumDbContext db, Guid vaultId, string userKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        if (ArkanumRoleCapabilities.CanReviewAllVaults(roles)) return true;
        return await db.VaultMembers.AnyAsync(m => m.VaultId == vaultId && m.UserKey == userKey);
    }

    public async Task<bool> CanInviteAsync(ArkanumDbContext db, Guid vaultId, string userKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        if (ArkanumRoleCapabilities.CanManageAnyVault(roles)) return true;
        if (ArkanumRoleCapabilities.IsAuditorOnly(roles)) return false;
        return await db.VaultMembers.AnyAsync(m => m.VaultId == vaultId && m.UserKey == userKey && m.CanInvite);
    }

    public async Task<bool> CanEditVaultMetadataAsync(ArkanumDbContext db, Guid vaultId, string userKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        if (ArkanumRoleCapabilities.CanManageAnyVault(roles)) return true;
        if (ArkanumRoleCapabilities.IsAuditorOnly(roles)) return false;
        return await db.Vaults.AnyAsync(v => v.Id == vaultId && v.CreatedByUserKey == userKey)
            || await db.VaultMembers.AnyAsync(m => m.VaultId == vaultId && m.UserKey == userKey && m.CanInvite);
    }

    public async Task<bool> CanAddOrChangeVaultContentsAsync(ArkanumDbContext db, Guid vaultId, string userKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        if (ArkanumRoleCapabilities.CanManageAnyVault(roles)) return true;
        if (ArkanumRoleCapabilities.IsAuditorOnly(roles)) return false;
        return await db.VaultMembers.AnyAsync(m => m.VaultId == vaultId && m.UserKey == userKey);
    }

    public async Task<bool> CanRevealSecretAsync(ArkanumDbContext db, Guid vaultId, string userKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        if (!ArkanumRoleCapabilities.CanRevealSecrets(roles)) return false;
        if (ArkanumRoleCapabilities.CanManageAnyVault(roles)) return true;
        return await db.VaultMembers.AnyAsync(m => m.VaultId == vaultId && m.UserKey == userKey);
    }

    public async Task UpdateVaultMetadataAsync(ArkanumDbContext db, Guid vaultId, string name, string description, string actorUserKey, IReadOnlyCollection<ArkanumRole> roles, DateTimeOffset now)
    {
        if (!await CanEditVaultMetadataAsync(db, vaultId, actorUserKey, roles)) throw new UnauthorizedAccessException("You do not have permission to edit this vault.");
        var trimmedName = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName)) throw new ArgumentException("Vault name is required.", nameof(name));
        var vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
        vault.Name = trimmedName;
        vault.Description = description.Trim();
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.VaultAssignmentChanged,
            ActorUserKey = actorUserKey,
            Target = vault.Name,
            OccurredAt = now,
            Message = "Vault metadata updated."
        });
        await db.SaveChangesAsync();
    }

    public async Task<VaultFolderEntity> CreateFolderAsync(ArkanumDbContext db, Guid vaultId, string name, string actorUserKey, DateTimeOffset now)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("Folder name is required.", nameof(name));
        if (await db.VaultFolders.AnyAsync(f => f.VaultId == vaultId && f.Name.ToLower() == normalized.ToLower()))
        {
            throw new InvalidOperationException("A folder with that name already exists in this vault.");
        }

        var vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
        var folder = new VaultFolderEntity
        {
            VaultId = vaultId,
            Name = normalized,
            CreatedAt = now
        };
        db.VaultFolders.Add(folder);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.VaultAssignmentChanged,
            ActorUserKey = actorUserKey,
            Target = vault.Name,
            OccurredAt = now,
            Message = $"Folder '{normalized}' was created in the vault."
        });
        await db.SaveChangesAsync();
        return folder;
    }

    public async Task InviteUserAsync(ArkanumDbContext db, Guid vaultId, string username, string actorUserKey, DateTimeOffset now)
    {
        var normalized = username.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("Username is required.", nameof(username));
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == normalized && u.AppAccessEnabled)
            ?? throw new InvalidOperationException("No enabled user exists with that username.");
        var vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
        if (!await db.VaultMembers.AnyAsync(m => m.VaultId == vaultId && m.UserKey == user.UserKey))
        {
            db.VaultMembers.Add(new VaultMemberEntity
            {
                VaultId = vaultId,
                UserKey = user.UserKey,
                CanInvite = false,
                AddedByUserKey = actorUserKey,
                AddedAt = now
            });
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.VaultAssignmentChanged,
                ActorUserKey = actorUserKey,
                Target = vault.Name,
                OccurredAt = now,
                Message = $"{normalized} was invited to the vault."
            });
            await db.SaveChangesAsync();
        }
    }

    private static async Task<IReadOnlyList<VaultSummary>> SummarizeVaultsAsync(ArkanumDbContext db, IReadOnlyList<VaultEntity> vaults)
    {
        var vaultIds = vaults.Select(v => v.Id).ToArray();
        var folders = await db.VaultFolders.AsNoTracking().Where(f => vaultIds.Contains(f.VaultId)).GroupBy(f => f.VaultId).Select(g => new { VaultId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.VaultId, x => x.Count);
        var items = await db.VaultItems.AsNoTracking().Where(i => vaultIds.Contains(i.VaultId)).GroupBy(i => i.VaultId).Select(g => new { VaultId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.VaultId, x => x.Count);
        return vaults.Select(v => new VaultSummary(v.Id, v.Name, v.Description, folders.GetValueOrDefault(v.Id), items.GetValueOrDefault(v.Id), v.CreatedAt)).ToArray();
    }

    private static string PersonalVaultName(string username) => $"{username.Trim()}'s Vault";
}

public sealed record VaultSummary(Guid Id, string Name, string Description, int FolderCount, int ItemCount, DateTimeOffset CreatedAt);
public sealed record VaultDetails(Guid Id, string Name, string Description, IReadOnlyList<VaultMemberSummary> Members, IReadOnlyList<VaultFolderDetails> Folders);
public sealed record VaultMemberSummary(string UserKey, string Username, string DisplayName, bool CanInvite);
public sealed record VaultFolderDetails(Guid Id, string Name, IReadOnlyList<VaultItemSummary> Items);
public sealed record VaultItemSummary(Guid Id, string Title, VaultItemType Type, string? Username, DateOnly? ResetNotificationDate);
