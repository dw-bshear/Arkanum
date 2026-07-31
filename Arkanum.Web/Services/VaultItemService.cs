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
