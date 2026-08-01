using System.Security.Claims;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Settings;
using Arkanum.Web.Pages.Vaults;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public sealed class Issue017ObjectAuthorizationTests
{
    [Fact]
    public async Task RevealSecretRequiresItemToBelongToTheAuthorizedVault()
    {
        await using var db = CreateDb(nameof(RevealSecretRequiresItemToBelongToTheAuthorizedVault));
        var crypto = Crypto();
        var service = new VaultItemService(crypto);
        var (allowedVault, _, otherItem) = await SeedTwoVaultsAsync(db, crypto);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RevealSecretAsync(db, allowedVault.Id, otherItem.Id, "owner-a", SecretAccessPurpose.Reveal, DateTimeOffset.UtcNow));

        Assert.Empty(await db.AuditEvents.Where(e => e.Type == AuditEventType.SecretReveal).ToListAsync());
    }

    [Fact]
    public async Task CopyPostForCrossVaultItemFailsClosedWithoutTempDataSecret()
    {
        await using var db = CreateDb(nameof(CopyPostForCrossVaultItemFailsClosedWithoutTempDataSecret));
        var crypto = Crypto();
        var (allowedVault, _, otherItem) = await SeedTwoVaultsAsync(db, crypto);
        var pageContext = PageContextFor("owner-a", ArkanumRole.User);
        var model = new DetailsModel(new VaultRepository(), new VaultItemService(crypto), new VaultExportService(crypto), db)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(pageContext.HttpContext, new DictionaryTempDataProvider()),
            ItemId = otherItem.Id,
            Title = otherItem.Title
        };

        var result = await model.OnPostCopyAsync(allowedVault.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.False(model.TempData.ContainsKey("RevealedSecret"));
        Assert.Empty(await db.AuditEvents.Where(e => e.Type == AuditEventType.SecretCopy).ToListAsync());
    }

    [Fact]
    public async Task ResetNotificationRequiresItemToBelongToTheAuthorizedVault()
    {
        await using var db = CreateDb(nameof(ResetNotificationRequiresItemToBelongToTheAuthorizedVault));
        var crypto = Crypto();
        var (allowedVault, _, otherItem) = await SeedTwoVaultsAsync(db, crypto);
        var originalDate = otherItem.ResetNotificationDate;
        var service = new VaultItemService(crypto);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.UpdateResetNotificationAsync(db, allowedVault.Id, otherItem.Id, new DateOnly(2026, 8, 15), "owner-a", DateTimeOffset.UtcNow));

        var unchanged = await db.VaultItems.SingleAsync(i => i.Id == otherItem.Id);
        Assert.Equal(originalDate, unchanged.ResetNotificationDate);
        Assert.Empty(await db.AuditEvents.Where(e => e.Type == AuditEventType.SecretUpdated).ToListAsync());
    }

    [Fact]
    public async Task CreatingCredentialRequiresFolderToBelongToTheTargetVault()
    {
        await using var db = CreateDb(nameof(CreatingCredentialRequiresFolderToBelongToTheTargetVault));
        var crypto = Crypto();
        var (allowedVault, otherFolder, _) = await SeedTwoVaultsAsync(db, crypto);
        var service = new VaultItemService(crypto);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateLoginCredentialAsync(db, allowedVault.Id, otherFolder.Id, "Forged Folder", "admin", "Issue017!", "owner-a", DateTimeOffset.UtcNow));

        Assert.Empty(await db.VaultItems.Where(i => i.Title == "Forged Folder").ToListAsync());
        Assert.Empty(await db.AuditEvents.Where(e => e.Type == AuditEventType.SecretCreated && e.Target == "Forged Folder").ToListAsync());
    }

    [Fact]
    public async Task CreatingSecureNoteRequiresFolderToBelongToTheTargetVault()
    {
        await using var db = CreateDb(nameof(CreatingSecureNoteRequiresFolderToBelongToTheTargetVault));
        var crypto = Crypto();
        var (allowedVault, otherFolder, _) = await SeedTwoVaultsAsync(db, crypto);
        var service = new VaultItemService(crypto);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CreateSecureNoteAsync(db, allowedVault.Id, otherFolder.Id, "Forged Note", "Issue017 note", "owner-a", DateTimeOffset.UtcNow));

        Assert.Empty(await db.VaultItems.Where(i => i.Title == "Forged Note").ToListAsync());
        Assert.Empty(await db.AuditEvents.Where(e => e.Type == AuditEventType.SecretCreated && e.Target == "Forged Note").ToListAsync());
    }

    [Fact]
    public async Task SettingsGroupAssignmentRejectsMissingGroupOrUserIds()
    {
        await using var db = CreateDb(nameof(SettingsGroupAssignmentRejectsMissingGroupOrUserIds));
        db.Users.Add(new AppUserEntity { UserKey = "admin", Username = "admin", DisplayName = "Admin", AppAccessEnabled = true, RolesCsv = "Administrator,User" });
        await db.SaveChangesAsync();
        var model = SettingsModel(db);
        model.PageContext = PageContextFor("admin", ArkanumRole.Administrator);
        model.GroupId = Guid.NewGuid();
        model.GroupUserKey = "missing-user";

        var result = await model.OnPostAssignGroupUserAsync();

        Assert.IsType<PageResult>(result);
        Assert.Empty(await db.UserGroupMembers.ToListAsync());
        Assert.True(model.ModelState.ContainsKey(nameof(model.GroupId)) || model.ModelState.ContainsKey(nameof(model.GroupUserKey)));
    }

    [Fact]
    public void SourceBindsVaultItemServiceQueriesToVaultAndValidatesSettingsIds()
    {
        var root = ProjectRoot();
        var service = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Services", "VaultItemService.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));
        var details = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml.cs"));

        Assert.Contains("i.Id == itemId && i.VaultId == vaultId", service);
        Assert.Contains("f.Id == folderId && f.VaultId == vaultId", service);
        Assert.Contains("ValidateGroupAssignmentAsync", settings);
        Assert.Contains("ValidateSubmittedUserKeyAsync", settings);
        Assert.Contains("RevealSecretAsync(db, id, ItemId", details);
        Assert.Contains("UpdateResetNotificationAsync(db, id, ItemId", details);
    }

    private static Arkanum.Web.Pages.Settings.IndexModel SettingsModel(ArkanumDbContext db)
    {
        var crypto = Crypto();
        return new Arkanum.Web.Pages.Settings.IndexModel(
            db,
            new DashboardService(),
            TestPortalSettingsServiceFactory.Create(),
            new AdminUserService(new LocalPasswordHasher()),
            new DirectoryLdapImportService(),
            new VaultExportService(crypto));
    }

    private static async Task<(VaultEntity AllowedVault, VaultFolderEntity OtherFolder, VaultItemEntity OtherItem)> SeedTwoVaultsAsync(ArkanumDbContext db, SecretEncryptionService crypto)
    {
        var now = DateTimeOffset.Parse("2026-07-31T22:00:00Z");
        var allowedVault = new VaultEntity { Name = "Allowed", Description = "Allowed", CreatedByUserKey = "owner-a", CreatedAt = now };
        var otherVault = new VaultEntity { Name = "Other", Description = "Other", CreatedByUserKey = "owner-b", CreatedAt = now };
        var allowedFolder = new VaultFolderEntity { VaultId = allowedVault.Id, Name = "Allowed General", CreatedAt = now };
        var otherFolder = new VaultFolderEntity { VaultId = otherVault.Id, Name = "Other General", CreatedAt = now };
        var encrypted = crypto.Encrypt("Issue017 sample value");
        var otherItem = new VaultItemEntity
        {
            VaultId = otherVault.Id,
            FolderId = otherFolder.Id,
            Type = VaultItemType.LoginCredential,
            Title = "Other firewall",
            Username = "other-admin",
            EncryptedSecret = encrypted.Ciphertext,
            EncryptionKeyId = encrypted.KeyId,
            EncryptionAlgorithm = encrypted.Algorithm,
            EncryptedAt = now,
            ResetNotificationDate = new DateOnly(2026, 8, 1)
        };

        db.Vaults.AddRange(allowedVault, otherVault);
        db.VaultFolders.AddRange(allowedFolder, otherFolder);
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = allowedVault.Id, UserKey = "owner-a", AddedByUserKey = "owner-a", AddedAt = now });
        db.VaultItems.Add(otherItem);
        await db.SaveChangesAsync();
        return (allowedVault, otherFolder, otherItem);
    }

    private static SecretEncryptionService Crypto() => new(new StaticMasterKeyProvider("issue017-master-key-material"));

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase($"issue017-{name}-{Guid.NewGuid():n}")
            .Options;
        return new ArkanumDbContext(options);
    }

    private static PageContext PageContextFor(string userKey, params ArkanumRole[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userKey), new(ClaimTypes.Name, userKey) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role.ToString())));
        return new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }

    private sealed class DictionaryTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object> _data = [];
        public IDictionary<string, object> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
            _data.Clear();
            foreach (var value in values) _data[value.Key] = value.Value;
        }
    }


    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Arkanum.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate Arkanum.sln.");
    }
}
