using System.IO.Compression;
using System.Text;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class PhaseDFeedbackTests
{
    [Fact]
    public void InstallersExposeRestoreModeAndRecoveryKeySelection()
    {
        var root = ProjectRoot();
        var linux = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));
        var linuxGui = File.ReadAllText(Path.Combine(root, "installers", "linux", "arkanum-gui-install.py"));
        var windows = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var windowsGui = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-ArkanumGui.ps1"));

        Assert.Contains("ACTION=restore", linux);
        Assert.Contains("CONCLAVE_RESTORE_KEY_FILE", linux);
        Assert.Contains("CONCLAVE_RESTORE_KEY", linux);
        Assert.Contains("The provided Recovery Key does not match this database", linux);
        Assert.Contains("Restore from database backup", linuxGui);
        Assert.Contains("Recovery Key file", linuxGui);
        Assert.Contains("Paste Recovery Key", linuxGui);

        Assert.Contains("ValidateSet('install','upgrade','repair','side-by-side','restore','auto')", windows);
        Assert.Contains("RestoreKeyFile", windows);
        Assert.Contains("RestoreKey", windows);
        Assert.Contains("The provided Recovery Key does not match this database", windows);
        Assert.Contains("Restore from database backup", windowsGui);
        Assert.Contains("Recovery Key file", windowsGui);
        Assert.Contains("Paste Recovery Key", windowsGui);
    }

    [Fact]
    public async Task CredentialResetReminderServiceSendsDueDigestToVaultMembersExceptAdministrators()
    {
        await using var db = CreateDb(nameof(CredentialResetReminderServiceSendsDueDigestToVaultMembersExceptAdministrators));
        await db.Database.EnsureCreatedAsync();
        var now = new DateTimeOffset(2026, 7, 7, 9, 0, 0, TimeSpan.Zero);
        var vault = new VaultEntity { Name = "Ops", Description = "Ops", CreatedByUserKey = "owner", CreatedAt = now };
        var folder = new VaultFolderEntity { VaultId = vault.Id, Name = "General", CreatedAt = now };
        db.Vaults.Add(vault);
        db.VaultFolders.Add(folder);
        db.Users.AddRange(
            new AppUserEntity { UserKey = "member", Username = "member", DisplayName = "Member", Email = "member@example.com", AppAccessEnabled = true, DirectoryPresent = true, RolesCsv = "User" },
            new AppUserEntity { UserKey = "admin", Username = "admin", DisplayName = "Admin", Email = "admin@example.com", AppAccessEnabled = true, DirectoryPresent = true, RolesCsv = "Administrator,User" });
        db.VaultMembers.AddRange(
            new VaultMemberEntity { VaultId = vault.Id, UserKey = "member", AddedByUserKey = "owner", AddedAt = now },
            new VaultMemberEntity { VaultId = vault.Id, UserKey = "admin", AddedByUserKey = "owner", AddedAt = now });
        db.VaultItems.Add(new VaultItemEntity
        {
            VaultId = vault.Id,
            FolderId = folder.Id,
            Type = VaultItemType.LoginCredential,
            Title = "Firewall admin",
            Username = "fwadmin",
            EncryptedSecret = "cipher",
            ResetNotificationDate = DateOnly.FromDateTime(now.DateTime)
        });
        await db.SaveChangesAsync();
        await new PortalSettingsService().EnsureDefaultsAsync(db);
        var sender = new RecordingEmailSender();
        var service = new CredentialResetReminderService(sender, new PortalSettingsService());

        var result = await service.SendDueRemindersAsync(db, now);

        Assert.Equal(1, result.MessagesSent);
        Assert.Single(sender.Messages);
        Assert.Equal("member@example.com", sender.Messages[0].To);
        Assert.DoesNotContain("admin@example.com", sender.Messages.Select(m => m.To));
        Assert.Contains("Firewall admin", sender.Messages[0].Body);
        Assert.DoesNotContain("cipher", sender.Messages[0].Body);
        Assert.Equal(1, await db.VaultItems.CountAsync(i => i.ResetNotificationSentAt != null));
    }

    [Fact]
    public async Task VaultExportServiceCreatesEncryptedZipWithCsvAndAuditWithoutSecretsInLog()
    {
        await using var db = CreateDb(nameof(VaultExportServiceCreatesEncryptedZipWithCsvAndAuditWithoutSecretsInLog));
        await db.Database.EnsureCreatedAsync();
        var now = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var vault = new VaultEntity { Name = "Ops", Description = "Ops", CreatedByUserKey = "owner", CreatedAt = now };
        var folder = new VaultFolderEntity { VaultId = vault.Id, Name = "General", CreatedAt = now };
        db.Vaults.Add(vault);
        db.VaultFolders.Add(folder);
        var crypto = new SecretEncryptionService(new StaticMasterKeyProvider("phase-d-export-key"));
        var encrypted = crypto.Encrypt("PlainSecret!2026");
        db.VaultItems.Add(new VaultItemEntity
        {
            VaultId = vault.Id,
            FolderId = folder.Id,
            Type = VaultItemType.LoginCredential,
            Title = "Firewall admin",
            Username = "fwadmin",
            EncryptedSecret = encrypted.Ciphertext,
            EncryptionKeyId = encrypted.KeyId,
            EncryptionAlgorithm = encrypted.Algorithm,
            ResetNotificationDate = new DateOnly(2026, 8, 1)
        });
        await db.SaveChangesAsync();
        var service = new VaultExportService(crypto);

        var export = await service.ExportVaultCsvZipAsync(db, vault.Id, "admin", "ExportPassphrase!2026", now);

        Assert.EndsWith(".zip", export.FileName);
        Assert.True(export.Content.Length > 100);
        using var zip = new ZipArchive(new MemoryStream(export.Content), ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
        Assert.Contains(zip.Entries, e => e.FullName == "credentials.csv.aes");
        Assert.DoesNotContain("PlainSecret!2026", Encoding.UTF8.GetString(export.Content));
        Assert.DoesNotContain("Firewall admin,fwadmin,PlainSecret!2026", Encoding.UTF8.GetString(export.Content));
        var audit = Assert.Single(await db.AuditEvents.ToListAsync());
        Assert.Contains("Vault export generated", audit.Message);
        Assert.DoesNotContain("PlainSecret!2026", audit.Message);
    }

    [Fact]
    public void VaultAndSettingsMarkupExposeResetReminderAndCsvExportControls()
    {
        var root = ProjectRoot();
        var vaultMarkup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));
        var settingsMarkup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var settingsModel = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));

        Assert.Contains("Reset notification", vaultMarkup);
        Assert.Contains("type=\"date\"", vaultMarkup);
        Assert.Contains("asp-page-handler=\"UpdateResetNotification\"", vaultMarkup);
        Assert.Contains("Export vault CSV", vaultMarkup);
        Assert.Contains("asp-page-handler=\"ExportVaultCsv\"", vaultMarkup);
        Assert.Contains("Export all vaults", settingsMarkup);
        Assert.Contains("encrypted ZIP", settingsMarkup);
        Assert.Contains("asp-page-handler=\"ExportAllVaultsCsv\"", settingsMarkup);
        Assert.Contains("OnPostExportAllVaultsCsvAsync", settingsModel);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }

    private static string ProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }
}
