using System.Text.Json;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public enum SecretAccessPurpose
{
    Reveal,
    Copy
}

public sealed class VaultItemService(SecretEncryptionService encryptionService)
{
    public async Task<VaultItemEntity> CreateLoginCredentialAsync(ArkanumDbContext db, Guid vaultId, Guid folderId, string title, string username, string password, string actorUserKey, DateTimeOffset now)
    {
        await EnsureFolderBelongsToVaultAsync(db, vaultId, folderId);
        var encrypted = encryptionService.Encrypt(password);
        var item = new VaultItemEntity
        {
            VaultId = vaultId,
            FolderId = folderId,
            Type = VaultItemType.LoginCredential,
            Title = title.Trim(),
            Username = username.Trim(),
            EncryptedSecret = encrypted.Ciphertext,
            EncryptionKeyId = encrypted.KeyId,
            EncryptionAlgorithm = encrypted.Algorithm,
            EncryptedAt = now
        };
        db.VaultItems.Add(item);
        db.CredentialVersions.Add(CreateVersion(item, 1, password, now));
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretCreated,
            ActorUserKey = actorUserKey,
            Target = item.Title,
            OccurredAt = now,
            Message = "Login credential created. Secret value was encrypted and not logged."
        });
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<VaultItemEntity> CreateSecureNoteAsync(ArkanumDbContext db, Guid vaultId, Guid folderId, string title, string note, string actorUserKey, DateTimeOffset now)
    {
        await EnsureFolderBelongsToVaultAsync(db, vaultId, folderId);
        var encrypted = encryptionService.Encrypt(note);
        var item = new VaultItemEntity
        {
            VaultId = vaultId,
            FolderId = folderId,
            Type = VaultItemType.SecureNote,
            Title = title.Trim(),
            EncryptedSecret = encrypted.Ciphertext,
            EncryptionKeyId = encrypted.KeyId,
            EncryptionAlgorithm = encrypted.Algorithm,
            EncryptedAt = now
        };
        db.VaultItems.Add(item);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretCreated,
            ActorUserKey = actorUserKey,
            Target = item.Title,
            OccurredAt = now,
            Message = "Secure note created. Secret value was encrypted and not logged."
        });
        await db.SaveChangesAsync();
        return item;
    }

    public async Task<string> RevealSecretAsync(ArkanumDbContext db, Guid vaultId, Guid itemId, string actorUserKey, SecretAccessPurpose purpose, DateTimeOffset now)
    {
        var item = await LoadVaultItemAsync(db, vaultId, itemId);
        var plaintext = encryptionService.Decrypt(new EncryptedSecretPayload(item.EncryptedSecret, item.EncryptionKeyId, item.EncryptionAlgorithm));
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = purpose == SecretAccessPurpose.Copy ? AuditEventType.SecretCopy : AuditEventType.SecretReveal,
            ActorUserKey = actorUserKey,
            Target = item.Title,
            OccurredAt = now,
            Message = purpose == SecretAccessPurpose.Copy ? "Secret value copied by authorized user." : "Secret value revealed by authorized user."
        });
        await db.SaveChangesAsync();
        return plaintext;
    }

    public async Task UpdateResetNotificationAsync(ArkanumDbContext db, Guid vaultId, Guid itemId, DateOnly? resetDate, string actorUserKey, DateTimeOffset now)
    {
        var item = await LoadVaultItemAsync(db, vaultId, itemId);
        item.ResetNotificationDate = resetDate;
        item.ResetNotificationSentAt = null;
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretUpdated,
            ActorUserKey = actorUserKey,
            Target = item.Title,
            OccurredAt = now,
            Message = resetDate is null
                ? "Credential reset notification date cleared. Secret value was not logged."
                : $"Credential reset notification date set to {resetDate:yyyy-MM-dd}. Secret value was not logged."
        });
        await db.SaveChangesAsync();
    }

    // Five saved versions TOTAL, including the current credential. Revision ordering is
    // monotonic per item and does not depend on wall-clock accuracy or SQLite date sorting.
    public async Task EditLoginCredentialAsync(ArkanumDbContext db, Guid vaultId, Guid itemId, Guid folderId,
        string title, string username, string? password, string actorUserKey, IReadOnlyCollection<ArkanumRole> roles, DateTimeOffset now)
    {
        await EnsureHistoryAccessAsync(db, vaultId, actorUserKey, roles, edit: true);
        var item = await LoadCredentialAsync(db, vaultId, itemId);
        await EnsureFolderBelongsToVaultAsync(db, vaultId, folderId);
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (title.Trim().Length > 256 || username.Trim().Length > 256) throw new ArgumentException("Title and username must be at most 256 characters.");
        var versions = await db.CredentialVersions.Where(v => v.VaultId == vaultId && v.ItemId == itemId).OrderByDescending(v => v.Revision).ToListAsync();
        var originalPassword = encryptionService.Decrypt(new EncryptedSecretPayload(item.EncryptedSecret, item.EncryptionKeyId, item.EncryptionAlgorithm));
        if (versions.Count == 0)
        {
            // Lazily preserve legacy data before changing any of its values.
            versions.Add(CreateVersion(item, 1, originalPassword, item.EncryptedAt));
            db.CredentialVersions.Add(versions[0]);
        }
        var revision = checked(versions[0].Revision + 1);
        var newPassword = string.IsNullOrEmpty(password) ? originalPassword : password;
        var encrypted = encryptionService.Encrypt(newPassword);
        item.Title = title.Trim();
        item.Username = username.Trim();
        item.FolderId = folderId;
        item.EncryptedSecret = encrypted.Ciphertext;
        item.EncryptionKeyId = encrypted.KeyId;
        item.EncryptionAlgorithm = encrypted.Algorithm;
        item.EncryptedAt = now;
        item.ResetNotificationSentAt = null;
        db.CredentialVersions.Add(CreateVersion(item, revision, newPassword, now));
        db.CredentialVersions.RemoveRange(versions.Skip(4));
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretUpdated, ActorUserKey = actorUserKey, Target = item.Id.ToString(), OccurredAt = now,
            Message = "Credential edited. Encrypted saved history retained (five versions including current); protected values not logged."
        });
        // EF's single SaveChanges transaction covers current value, snapshot, pruning and audit.
        // The ciphertext concurrency token and unique item/revision prevent lost concurrent saves.
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<CredentialVersionSummary>> ListCredentialHistoryAsync(ArkanumDbContext db, Guid vaultId, Guid itemId,
        string actorUserKey, IReadOnlyCollection<ArkanumRole> roles)
    {
        await EnsureHistoryAccessAsync(db, vaultId, actorUserKey, roles, edit: false);
        await LoadCredentialAsync(db, vaultId, itemId);
        return await db.CredentialVersions.AsNoTracking().Where(v => v.VaultId == vaultId && v.ItemId == itemId)
            .OrderByDescending(v => v.Revision).Take(5)
            .Select(v => new CredentialVersionSummary(v.Id, v.Revision, v.SavedAt)).ToListAsync();
    }

    public async Task<CredentialVersionValues> RevealCredentialVersionAsync(ArkanumDbContext db, Guid vaultId, Guid itemId, Guid versionId,
        string actorUserKey, IReadOnlyCollection<ArkanumRole> roles, DateTimeOffset now)
    {
        await EnsureHistoryAccessAsync(db, vaultId, actorUserKey, roles, edit: false);
        await LoadCredentialAsync(db, vaultId, itemId);
        var version = await db.CredentialVersions.AsNoTracking().SingleOrDefaultAsync(v => v.VaultId == vaultId && v.ItemId == itemId && v.Id == versionId)
            ?? throw new UnauthorizedAccessException("The saved version does not belong to the authorized credential.");
        var values = JsonSerializer.Deserialize<CredentialVersionValues>(encryptionService.Decrypt(
            new EncryptedSecretPayload(version.EncryptedPayload, version.EncryptionKeyId, version.EncryptionAlgorithm)))
            ?? throw new InvalidOperationException("Saved version could not be read.");
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SecretReveal, ActorUserKey = actorUserKey, Target = itemId.ToString(), OccurredAt = now,
            Message = "Saved credential version revealed by authorized user. Protected values not logged."
        });
        await db.SaveChangesAsync();
        return values;
    }

    private CredentialVersionEntity CreateVersion(VaultItemEntity item, long revision, string password, DateTimeOffset savedAt)
    {
        var encrypted = encryptionService.Encrypt(JsonSerializer.Serialize(new CredentialVersionValues(item.Title, item.Username ?? string.Empty, password)));
        return new CredentialVersionEntity
        {
            VaultId = item.VaultId, ItemId = item.Id, Revision = revision, SavedAt = savedAt,
            EncryptedPayload = encrypted.Ciphertext, EncryptionKeyId = encrypted.KeyId, EncryptionAlgorithm = encrypted.Algorithm
        };
    }

    private static async Task<VaultItemEntity> LoadCredentialAsync(ArkanumDbContext db, Guid vaultId, Guid itemId)
    {
        var item = await LoadVaultItemAsync(db, vaultId, itemId);
        if (item.Type != VaultItemType.LoginCredential) throw new UnauthorizedAccessException("This action requires a login credential.");
        return item;
    }

    private static async Task EnsureHistoryAccessAsync(ArkanumDbContext db, Guid vaultId, string actorUserKey,
        IReadOnlyCollection<ArkanumRole> roles, bool edit)
    {
        var repository = new VaultRepository();
        if (string.IsNullOrWhiteSpace(actorUserKey) || actorUserKey == "anonymous" || !ArkanumRoleCapabilities.CanRevealSecrets(roles)
            || !await db.Vaults.AnyAsync(v => v.Id == vaultId && !v.IsRetired)
            || !(edit ? await repository.CanAddOrChangeVaultContentsAsync(db, vaultId, actorUserKey, roles)
                : await repository.CanRevealSecretAsync(db, vaultId, actorUserKey, roles)))
            throw new UnauthorizedAccessException("You do not have permission to access credential versions.");
    }

    private static async Task EnsureFolderBelongsToVaultAsync(ArkanumDbContext db, Guid vaultId, Guid folderId)
    {
        if (!await db.VaultFolders.AnyAsync(f => f.Id == folderId && f.VaultId == vaultId))
        {
            throw new UnauthorizedAccessException("The selected folder does not belong to the target vault.");
        }
    }

    private static async Task<VaultItemEntity> LoadVaultItemAsync(ArkanumDbContext db, Guid vaultId, Guid itemId)
    {
        return await db.VaultItems.SingleOrDefaultAsync(i => i.Id == itemId && i.VaultId == vaultId)
            ?? throw new UnauthorizedAccessException("The selected item does not belong to the authorized vault.");
    }
}

public sealed record CredentialVersionSummary(Guid Id, long Revision, DateTimeOffset SavedAt);
public sealed record CredentialVersionValues(string Title, string Username, string Password);
