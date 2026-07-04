using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Iteration004Tests
{
    [Fact]
    public async Task Standard_user_gets_one_default_personal_vault_and_only_sees_assigned_vaults()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var user = new AppUserEntity { UserKey = "user-key", Username = "standard", DisplayName = "standard", AppAccessEnabled = true, RolesCsv = nameof(ArkanumRole.User) };
        db.Users.Add(user);
        await repository.CreateVaultAsync(db, "Admin shared", "Not assigned to standard user", "admin-key", DateTimeOffset.Parse("2026-06-27T01:00:00Z"));
        await db.SaveChangesAsync();

        var personal = await repository.EnsurePersonalVaultAsync(db, user.UserKey, user.Username, DateTimeOffset.Parse("2026-06-27T02:00:00Z"));
        var secondCall = await repository.EnsurePersonalVaultAsync(db, user.UserKey, user.Username, DateTimeOffset.Parse("2026-06-27T03:00:00Z"));
        var visible = await repository.ListVaultsForUserAsync(db, user.UserKey, [ArkanumRole.User]);

        Assert.Equal(personal.Id, secondCall.Id);
        var single = Assert.Single(visible);
        Assert.Equal("standard's Vault", single.Name);
        Assert.Contains(await db.VaultMembers.ToListAsync(), m => m.VaultId == personal.Id && m.UserKey == user.UserKey && m.CanInvite);
    }

    [Fact]
    public async Task Vault_owner_can_invite_another_enabled_user_to_their_personal_vault()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var owner = new AppUserEntity { UserKey = "owner-key", Username = "owner", DisplayName = "owner", AppAccessEnabled = true, RolesCsv = nameof(ArkanumRole.User) };
        var teammate = new AppUserEntity { UserKey = "teammate-key", Username = "teammate", DisplayName = "teammate", AppAccessEnabled = true, RolesCsv = nameof(ArkanumRole.User) };
        db.Users.AddRange(owner, teammate);
        var personal = await repository.EnsurePersonalVaultAsync(db, owner.UserKey, owner.Username, DateTimeOffset.Parse("2026-06-27T02:00:00Z"));

        await repository.InviteUserAsync(db, personal.Id, "teammate", owner.UserKey, DateTimeOffset.Parse("2026-06-27T03:00:00Z"));
        var teammateVaults = await repository.ListVaultsForUserAsync(db, teammate.UserKey, [ArkanumRole.User]);

        Assert.Contains(teammateVaults, v => v.Id == personal.Id);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.VaultAssignmentChanged && e.Target == personal.Name && e.Message.Contains("teammate"));
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
