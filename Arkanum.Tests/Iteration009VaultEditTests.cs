using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Arkanum.Tests;

public class Iteration009VaultEditTests
{
    [Fact]
    public async Task Vault_creator_or_manager_can_update_vault_name_and_description()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var owner = await CreateUserAsync(db, "owner");
        var manager = await CreateUserAsync(db, "manager");
        var member = await CreateUserAsync(db, "member");
        var vault = await repository.CreateVaultAsync(db, "Old vault", "Old description", owner.UserKey, DateTimeOffset.Parse("2026-06-27T10:00:00Z"));
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = manager.UserKey, CanInvite = true, AddedByUserKey = owner.UserKey, AddedAt = DateTimeOffset.UtcNow });
        db.VaultMembers.Add(new VaultMemberEntity { VaultId = vault.Id, UserKey = member.UserKey, CanInvite = false, AddedByUserKey = owner.UserKey, AddedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        Assert.True(await repository.CanEditVaultMetadataAsync(db, vault.Id, owner.UserKey, [ArkanumRole.User]));
        Assert.True(await repository.CanEditVaultMetadataAsync(db, vault.Id, manager.UserKey, [ArkanumRole.User]));
        Assert.False(await repository.CanEditVaultMetadataAsync(db, vault.Id, member.UserKey, [ArkanumRole.User]));

        await repository.UpdateVaultMetadataAsync(db, vault.Id, "Renamed vault", "Updated description", manager.UserKey, [ArkanumRole.User], DateTimeOffset.Parse("2026-06-27T11:00:00Z"));
        var saved = await db.Vaults.SingleAsync(v => v.Id == vault.Id);

        Assert.Equal("Renamed vault", saved.Name);
        Assert.Equal("Updated description", saved.Description);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.VaultAssignmentChanged && e.Target == "Renamed vault" && e.Message.Contains("metadata updated"));
    }

    [Fact]
    public void Vault_pages_show_access_list_copy_and_metadata_edit_form()
    {
        var root = ProjectRoot();
        var index = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Index.cshtml"));
        var details = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Vaults", "Details.cshtml"));

        Assert.Contains("Vault Access List", index);
        Assert.Contains("Your available Vaults are listed below. If you need an additional vault, select 'Create Vault' to establish a new credential vault.", index);
        Assert.DoesNotContain("Vaults, folders, and items", index);
        Assert.DoesNotContain("Open existing vaults, review folders and items", index);
        Assert.Contains("asp-page-handler=\"UpdateMetadata\"", details);
        Assert.Contains("CanEditVaultMetadata", details);
        Assert.Contains("Vault name", details);
        Assert.Contains("Vault description", details);
    }

    private static async Task<AppUserEntity> CreateUserAsync(ArkanumDbContext db, string username)
    {
        var user = new AppUserEntity { Username = username, DisplayName = username, Email = $"{username}@email.com", Source = UserSource.Local, DirectoryPresent = true, AppAccessEnabled = true, RolesCsv = "User" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
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
