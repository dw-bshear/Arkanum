using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Pages.Vaults;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class VaultRetirementIssueTests
{
    [Fact]
    public async Task AdminCanRetireVaultAndNormalVaultListsHideItWithoutDeletingContents()
    {
        await using var db = CreateDb(nameof(AdminCanRetireVaultAndNormalVaultListsHideItWithoutDeletingContents));
        var repository = new VaultRepository();
        var vault = await repository.CreateVaultAsync(db, "Smoke Test Vault", "temporary", "owner", DateTimeOffset.Parse("2026-08-01T12:00:00Z"));
        var folder = await db.VaultFolders.SingleAsync(f => f.VaultId == vault.Id);
        db.Users.Add(new AppUserEntity { UserKey = "member", Username = "member", DisplayName = "Member", Email = "member@email.com", AppAccessEnabled = true, DirectoryPresent = true, RolesCsv = "User" });
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = "member", AddedByUserKey = "owner", AddedAt = DateTimeOffset.Parse("2026-08-01T12:00:00Z") });
        db.VaultItems.Add(new VaultItemEntity { VaultId = vault.Id, FolderId = folder.Id, Type = VaultItemType.LoginCredential, Title = "Do not delete", Username = "smoke", EncryptedSecret = "ciphertext" });
        await db.SaveChangesAsync();

        await repository.RetireVaultAsync(db, vault.Id, "admin", [ArkanumRole.Administrator], vault.Name, DateTimeOffset.Parse("2026-08-02T12:00:00Z"));

        var retired = await db.Vaults.SingleAsync(v => v.Id == vault.Id);
        Assert.True(retired.IsRetired);
        Assert.Equal("admin", retired.RetiredByUserKey);
        Assert.Contains("exact-name confirmation", retired.RetirementReason);
        Assert.Empty(await repository.ListVaultsForUserAsync(db, "member", [ArkanumRole.User]));
        Assert.Empty(await repository.ListVaultsAsync(db));
        Assert.Equal(1, await db.VaultItems.CountAsync(i => i.VaultId == vault.Id));
        var audit = Assert.Single(await db.AuditEvents.Where(a => a.Message.Contains("retired")).ToListAsync());
        Assert.Contains("Smoke Test Vault", audit.Target);
        Assert.DoesNotContain("ciphertext", audit.Message);
    }

    [Fact]
    public async Task NonAdminCannotRetireVaultEvenWhenVaultOwnerOrManager()
    {
        await using var db = CreateDb(nameof(NonAdminCannotRetireVaultEvenWhenVaultOwnerOrManager));
        var repository = new VaultRepository();
        var vault = await repository.CreateVaultAsync(db, "Production Vault", "real data", "owner", DateTimeOffset.Parse("2026-08-01T12:00:00Z"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.RetireVaultAsync(db, vault.Id, "owner", [ArkanumRole.User], vault.Name, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.RetireVaultAsync(db, vault.Id, "manager", [ArkanumRole.VaultManager, ArkanumRole.User], vault.Name, DateTimeOffset.UtcNow));
        Assert.False((await db.Vaults.SingleAsync(v => v.Id == vault.Id)).IsRetired);
    }

    [Fact]
    public async Task RetireConfirmationRequiresExactVaultName()
    {
        await using var db = CreateDb(nameof(RetireConfirmationRequiresExactVaultName));
        var repository = new VaultRepository();
        var vault = await repository.CreateVaultAsync(db, "Exact Name Vault", "temporary", "owner", DateTimeOffset.Parse("2026-08-01T12:00:00Z"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RetireVaultAsync(db, vault.Id, "admin", [ArkanumRole.Administrator], "wrong name", DateTimeOffset.UtcNow));
        Assert.False((await db.Vaults.SingleAsync(v => v.Id == vault.Id)).IsRetired);
    }

    [Fact]
    public void VaultUiExposesSafeRetirementFlowOnlyForAdmins()
    {
        var root = ProjectRoot();
        var indexMarkup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Index.cshtml"));
        var indexModel = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Index.cshtml.cs"));
        var detailsMarkup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));
        var schema = File.ReadAllText(Path.Combine(root, "installers", "sql", "arkanum-schema.sql"));

        Assert.Contains("CanRetireVaults", indexModel);
        Assert.Contains("OnPostRetireAsync", indexModel);
        Assert.Contains("Archive Vault", indexMarkup);
        Assert.Contains("This is safer than hard delete", indexMarkup);
        Assert.Contains("name=\"RetireConfirmationName\"", indexMarkup);
        Assert.Contains("asp-page-handler=\"Retire\"", indexMarkup);
        Assert.DoesNotContain("Hard delete vault", indexMarkup);
        Assert.Contains("retired vault", detailsMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsRetired", schema);
        Assert.Contains("RetiredAt", schema);
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
}
