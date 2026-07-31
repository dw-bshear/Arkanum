using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Iteration003Tests
{
    [Fact]
    public void Secret_encryption_round_trips_without_storing_plaintext()
    {
        var service = new SecretEncryptionService(new StaticMasterKeyProvider("0123456789abcdef0123456789abcdef"));

        var encrypted = service.Encrypt("SuperSecret!234", "env:test");
        var decrypted = service.Decrypt(encrypted);

        Assert.Equal("SuperSecret!234", decrypted);
        Assert.DoesNotContain("SuperSecret", encrypted.Ciphertext);
        Assert.Equal("AES-256-GCM", encrypted.Algorithm);
        Assert.Equal("env:test", encrypted.KeyId);
    }

    [Fact]
    public async Task Vault_item_service_creates_encrypted_login_credential_and_audit_event()
    {
        await using var db = CreateDb();
        var vaultRepository = new VaultRepository();
        var vault = await vaultRepository.CreateVaultAsync(db, "Operations", "", "admin-key", DateTimeOffset.Parse("2026-06-26T12:00:00Z"));
        var folder = await db.VaultFolders.SingleAsync(f => f.VaultId == vault.Id);
        var service = new VaultItemService(new SecretEncryptionService(new StaticMasterKeyProvider("0123456789abcdef0123456789abcdef")));

        var item = await service.CreateLoginCredentialAsync(db, vault.Id, folder.Id, "Firewall", "fw-admin", "SuperSecret!234", "admin-key", DateTimeOffset.Parse("2026-06-26T12:05:00Z"));

        Assert.Equal(VaultItemType.LoginCredential, item.Type);
        Assert.Equal("Firewall", item.Title);
        Assert.Equal("fw-admin", item.Username);
        Assert.DoesNotContain("SuperSecret", item.EncryptedSecret);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.SecretCreated && e.Target == "Firewall");
    }

    [Fact]
    public async Task Reveal_and_copy_secret_decrypt_value_and_record_separate_audit_events()
    {
        await using var db = CreateDb();
        var vaultRepository = new VaultRepository();
        var vault = await vaultRepository.CreateVaultAsync(db, "Operations", "", "admin-key", DateTimeOffset.Parse("2026-06-26T12:00:00Z"));
        var folder = await db.VaultFolders.SingleAsync(f => f.VaultId == vault.Id);
        var service = new VaultItemService(new SecretEncryptionService(new StaticMasterKeyProvider("0123456789abcdef0123456789abcdef")));
        var item = await service.CreateLoginCredentialAsync(db, vault.Id, folder.Id, "Firewall", "fw-admin", "SuperSecret!234", "admin-key", DateTimeOffset.Parse("2026-06-26T12:05:00Z"));

        var revealed = await service.RevealSecretAsync(db, vault.Id, item.Id, "auditor-key", SecretAccessPurpose.Reveal, DateTimeOffset.Parse("2026-06-26T12:06:00Z"));
        var copied = await service.RevealSecretAsync(db, vault.Id, item.Id, "auditor-key", SecretAccessPurpose.Copy, DateTimeOffset.Parse("2026-06-26T12:07:00Z"));

        Assert.Equal("SuperSecret!234", revealed);
        Assert.Equal("SuperSecret!234", copied);
        var events = await db.AuditEvents.ToListAsync();
        Assert.Contains(events, e => e.Type == AuditEventType.SecretReveal && e.Target == "Firewall");
        Assert.Contains(events, e => e.Type == AuditEventType.SecretCopy && e.Target == "Firewall");
        Assert.DoesNotContain(events, e => e.Message.Contains("SuperSecret"));
    }

    private static ArkanumDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("n"))
            .Options;
        var db = new ArkanumDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
