using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Phase3RoleSeparationTests
{
    [Fact]
    public void RoleCatalogDocumentsEverySupportedRoleAndMultiRoleCapabilities()
    {
        foreach (var role in Enum.GetValues<ArkanumRole>())
        {
            Assert.Contains(ArkanumRoleCatalog.Descriptions, description => description.Role == role && !string.IsNullOrWhiteSpace(description.Summary));
        }

        var multiRole = new[] { ArkanumRole.User, ArkanumRole.Auditor, ArkanumRole.VaultManager };
        Assert.True(ArkanumRoleCapabilities.CanReviewAllVaults(multiRole));
        Assert.True(ArkanumRoleCapabilities.CanManageAnyVault(multiRole));
        Assert.True(ArkanumRoleCapabilities.CanRevealSecrets(multiRole));
        Assert.False(ArkanumRoleCapabilities.IsAuditorOnly(multiRole));
    }

    [Fact]
    public void AuditorOnlyRoleCanReviewButCannotChangeOrRevealVaultContents()
    {
        var roles = new[] { ArkanumRole.User, ArkanumRole.Auditor };

        Assert.True(ArkanumRoleCapabilities.CanReviewAllVaults(roles));
        Assert.True(ArkanumRoleCapabilities.IsAuditorOnly(roles));
        Assert.False(ArkanumRoleCapabilities.CanCreateVault(roles));
        Assert.False(ArkanumRoleCapabilities.CanRevealSecrets(roles));
        Assert.False(ArkanumRoleCapabilities.CanManageAnyVault(roles));
    }

    [Fact]
    public async Task VaultRepositoryAllowsAuditorInventoryButBlocksRevealAndChangeCapabilities()
    {
        await using var db = CreateDb(nameof(VaultRepositoryAllowsAuditorInventoryButBlocksRevealAndChangeCapabilities));
        var vault = await SeedVaultAsync(db);
        var repository = new VaultRepository();
        var auditorRoles = new[] { ArkanumRole.User, ArkanumRole.Auditor };

        Assert.True(await repository.CanAccessVaultAsync(db, vault.Id, "auditor-key", auditorRoles));
        var visibleVaults = await repository.ListVaultsForUserAsync(db, "auditor-key", auditorRoles);
        Assert.Contains(visibleVaults, v => v.Id == vault.Id);
        Assert.False(await repository.CanInviteAsync(db, vault.Id, "auditor-key", auditorRoles));
        Assert.False(await repository.CanEditVaultMetadataAsync(db, vault.Id, "auditor-key", auditorRoles));
        Assert.False(await repository.CanAddOrChangeVaultContentsAsync(db, vault.Id, "auditor-key", auditorRoles));
        Assert.False(await repository.CanRevealSecretAsync(db, vault.Id, "auditor-key", auditorRoles));
    }

    [Fact]
    public async Task VaultManagerCanManageVaultsWithoutAdministratorRole()
    {
        await using var db = CreateDb(nameof(VaultManagerCanManageVaultsWithoutAdministratorRole));
        var vault = await SeedVaultAsync(db);
        var repository = new VaultRepository();
        var managerRoles = new[] { ArkanumRole.User, ArkanumRole.VaultManager };

        Assert.True(await repository.CanAccessVaultAsync(db, vault.Id, "manager-key", managerRoles));
        Assert.True(await repository.CanInviteAsync(db, vault.Id, "manager-key", managerRoles));
        Assert.True(await repository.CanEditVaultMetadataAsync(db, vault.Id, "manager-key", managerRoles));
        Assert.True(await repository.CanAddOrChangeVaultContentsAsync(db, vault.Id, "manager-key", managerRoles));
        Assert.True(await repository.CanRevealSecretAsync(db, vault.Id, "manager-key", managerRoles));
        Assert.True(ArkanumRoleCapabilities.CanManageAnyVault(managerRoles));
        Assert.False(ArkanumRoleCapabilities.CanManageUsersAndSettings(managerRoles));
    }

    [Fact]
    public async Task AdminUserServicePreservesMultipleAssignedRoles()
    {
        await using var db = CreateDb(nameof(AdminUserServicePreservesMultipleAssignedRoles));
        var service = new AdminUserService(new LocalPasswordHasher());

        await service.CreateLocalUserAsync(db, "multi-role", "CorrectHorse!2026", [ArkanumRole.Auditor, ArkanumRole.VaultManager], "admin-key", DateTimeOffset.UtcNow);

        var user = await db.Users.SingleAsync(u => u.Username == "multi-role");
        Assert.Contains("Auditor", user.RolesCsv);
        Assert.Contains("VaultManager", user.RolesCsv);
        Assert.Contains("User", user.RolesCsv);
    }

    [Fact]
    public void SettingsMarkupIncludesRoleDescriptionsAndMultiRoleNote()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));

        Assert.Contains("Role descriptions", markup);
        Assert.Contains("Multiple roles are supported", markup);
        Assert.Contains("roleDescription.Summary", markup);
    }

    private static async Task<VaultEntity> SeedVaultAsync(ArkanumDbContext db)
    {
        var vault = new VaultEntity { Name = "Ops", Description = "Operations", CreatedByUserKey = "owner-key", CreatedAt = DateTimeOffset.UtcNow };
        db.Vaults.Add(vault);
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = "owner-key", CanInvite = true, AddedByUserKey = "owner-key", AddedAt = DateTimeOffset.UtcNow });
        db.VaultFolders.Add(new VaultFolderEntity { VaultId = vault.Id, Name = "General", CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return vault;
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
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Arkanum.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find project root.");
    }
}
