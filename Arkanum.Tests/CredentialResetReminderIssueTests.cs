using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class CredentialResetReminderIssueTests
{
    [Fact]
    public async Task ReminderServiceHonorsEnabledSettingAndDoesNotMarkItemsWhenDisabled()
    {
        await using var db = CreateDb(nameof(ReminderServiceHonorsEnabledSettingAndDoesNotMarkItemsWhenDisabled));
        var (_, item) = await SeedDueCredentialAsync(db, "member", "member@email.com", "User", "Core router", "cipher-disabled");
        await new PortalSettingsService().EnsureDefaultsAsync(db);
        await SetSettingAsync(db, PortalSettingKeys.CredentialResetReminderEnabled, "false");
        var sender = new RecordingEmailSender();
        var service = new CredentialResetReminderService(sender, new PortalSettingsService());

        var result = await service.SendDueRemindersAsync(db, new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal(1, result.DueItems);
        Assert.Equal(0, result.MessagesSent);
        Assert.Empty(sender.Messages);
        Assert.Null((await db.VaultItems.SingleAsync(i => i.Id == item.Id)).ResetNotificationSentAt);
    }

    [Fact]
    public async Task ReminderServiceSendsOneDigestPerUserAcrossVaultsAndAuditsWithoutSecrets()
    {
        await using var db = CreateDb(nameof(ReminderServiceSendsOneDigestPerUserAcrossVaultsAndAuditsWithoutSecrets));
        var now = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
        await SeedDueCredentialAsync(db, "member", "member@email.com", "User", "Firewall admin", "cipher-fw", vaultName: "Ops");
        await SeedDueCredentialAsync(db, "member", "member@email.com", "User", "VPN breakglass", "cipher-vpn", vaultName: "Network");
        await SeedDueCredentialAsync(db, "admin", "admin@email.com", "Administrator,User", "Domain admin", "cipher-admin", vaultName: "Ops");
        await SeedDueCredentialAsync(db, "noemail", "", "User", "Email-less login", "cipher-noemail", vaultName: "Ops");
        await new PortalSettingsService().EnsureDefaultsAsync(db);
        await SetSettingAsync(db, PortalSettingKeys.CredentialResetReminderSubject, "Reset digest due {{DueDate}}");
        await SetSettingAsync(db, PortalSettingKeys.CredentialResetReminderBody, "Hello {{displayName}}\n{{CredentialList}}\nNo secrets here.");
        var sender = new RecordingEmailSender();
        var service = new CredentialResetReminderService(sender, new PortalSettingsService());

        var result = await service.SendDueRemindersAsync(db, now);

        Assert.Equal(4, result.DueItems);
        Assert.Equal(1, result.MessagesSent);
        var message = Assert.Single(sender.Messages);
        Assert.Equal("member@email.com", message.To);
        Assert.Contains("Firewall admin", message.Body);
        Assert.Contains("VPN breakglass", message.Body);
        Assert.Contains("Ops", message.Body);
        Assert.Contains("Network", message.Body);
        Assert.DoesNotContain("cipher-fw", message.Body);
        Assert.DoesNotContain("cipher-vpn", message.Body);
        Assert.DoesNotContain("cipher-admin", message.Body);
        Assert.DoesNotContain("admin@email.com", sender.Messages.Select(m => m.To));
        Assert.Equal(4, await db.VaultItems.CountAsync(i => i.ResetNotificationSentAt == now));
        var audits = await db.AuditEvents.Where(a => a.Message.Contains("Credential reset reminder sent")).ToListAsync();
        Assert.Equal(4, audits.Count);
        Assert.All(audits, audit =>
        {
            Assert.DoesNotContain("cipher-", audit.Message);
            Assert.Contains("Secret value was not included", audit.Message);
        });
    }

    [Fact]
    public void SettingsExposeCredentialResetReminderControlsAndScheduler()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var model = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Services", "PortalSettingsService.cs"));
        var program = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Program.cs"));

        Assert.Contains("Enable credential reset reminders", markup);
        Assert.Contains("Credential reset reminder daily send time", markup);
        Assert.Contains("Reminder subject template", markup);
        Assert.Contains("Reminder body template", markup);
        Assert.Contains("SMTP username", markup);
        Assert.Contains("SMTP password", markup);
        Assert.Contains("asp-page-handler=\"CredentialResetReminders\"", markup);
        Assert.Contains("OnPostCredentialResetRemindersAsync", model);
        Assert.Contains("CredentialResetReminderDailySendTime", settings);
        Assert.Contains("CredentialResetReminderSettings", settings);
        Assert.Contains("CredentialResetReminderHostedService", program);
        Assert.Contains("SmtpEmailSender", program);
    }

    private static async Task<(VaultEntity Vault, VaultItemEntity Item)> SeedDueCredentialAsync(ArkanumDbContext db, string userKey, string email, string roles, string title, string cipher, string vaultName = "Ops")
    {
        var now = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);
        var vault = new VaultEntity { Name = vaultName, Description = vaultName, CreatedByUserKey = "owner", CreatedAt = now };
        var folder = new VaultFolderEntity { VaultId = vault.Id, Name = "General", CreatedAt = now };
        db.Vaults.Add(vault);
        db.VaultFolders.Add(folder);
        if (!await db.Users.AnyAsync(u => u.UserKey == userKey))
        {
            db.Users.Add(new AppUserEntity { UserKey = userKey, Username = userKey, DisplayName = userKey, Email = email, AppAccessEnabled = true, DirectoryPresent = true, RolesCsv = roles });
        }
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = userKey, AddedByUserKey = "owner", AddedAt = now });
        var item = new VaultItemEntity
        {
            VaultId = vault.Id,
            FolderId = folder.Id,
            Type = VaultItemType.LoginCredential,
            Title = title,
            Username = title.ToLowerInvariant().Replace(' ', '.'),
            EncryptedSecret = cipher,
            ResetNotificationDate = DateOnly.FromDateTime(now.DateTime)
        };
        db.VaultItems.Add(item);
        await db.SaveChangesAsync();
        return (vault, item);
    }

    private static async Task SetSettingAsync(ArkanumDbContext db, string key, string value)
    {
        var setting = await db.PortalSettings.SingleAsync(s => s.SettingKey == key);
        setting.Value = value;
        await db.SaveChangesAsync();
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
