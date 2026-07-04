using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Iteration002Tests
{
    [Fact]
    public async Task Vault_repository_can_load_vault_details_with_folders_and_items()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();
        var vault = await repository.CreateVaultAsync(db, "Operations", "Shared operational credentials", "admin-key", DateTimeOffset.Parse("2026-06-26T12:10:00Z"));
        var folder = await db.VaultFolders.SingleAsync(f => f.VaultId == vault.Id);
        db.VaultItems.Add(new VaultItemEntity
        {
            VaultId = vault.Id,
            FolderId = folder.Id,
            Type = VaultItemType.LoginCredential,
            Title = "Firewall admin",
            Username = "fw-admin",
            EncryptedSecret = "ciphertext"
        });
        await db.SaveChangesAsync();

        var details = await repository.GetVaultDetailsAsync(db, vault.Id);

        Assert.NotNull(details);
        Assert.Equal("Operations", details!.Name);
        var detailFolder = Assert.Single(details.Folders);
        Assert.Equal("General", detailFolder.Name);
        Assert.Equal("Firewall admin", Assert.Single(detailFolder.Items).Title);
    }

    [Fact]
    public async Task Admin_user_service_creates_enabled_local_admin_user_with_selected_roles()
    {
        await using var db = CreateDb();
        var service = new AdminUserService(new LocalPasswordHasher());

        var user = await service.CreateLocalUserAsync(db, "vault-manager", "Vault Manager!234", [ArkanumRole.User, ArkanumRole.VaultManager], "admin-key", DateTimeOffset.Parse("2026-06-26T13:00:00Z"));

        Assert.True(user.AppAccessEnabled);
        Assert.Equal(UserSource.Local, user.Source);
        Assert.Contains(ArkanumRole.VaultManager, user.Roles);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.UserEnabled && e.Target == "vault-manager");
    }

    [Fact]
    public async Task Dashboard_settings_can_disable_widgets_and_role_visibility_filters_them()
    {
        await using var db = CreateDb();
        var service = new DashboardService();
        db.DashboardWidgets.AddRange(
            new DashboardWidgetEntity { WidgetKey = DashboardWidgetKeys.UserManagement, Title = "User Management", Enabled = true, RolesCsv = "Administrator" },
            new DashboardWidgetEntity { WidgetKey = DashboardWidgetKeys.AuditLogs, Title = "Audit Logs", Enabled = true, RolesCsv = "Auditor,Administrator" },
            new DashboardWidgetEntity { WidgetKey = DashboardWidgetKeys.Vaults, Title = "Vaults", Enabled = false, RolesCsv = "User,VaultManager,Auditor,Administrator" });
        await db.SaveChangesAsync();

        var adminWidgets = await service.GetVisibleWidgetsAsync(db, [ArkanumRole.Administrator]);
        var userWidgets = await service.GetVisibleWidgetsAsync(db, [ArkanumRole.User]);

        Assert.Contains(adminWidgets, w => w.WidgetKey == DashboardWidgetKeys.UserManagement);
        Assert.Contains(adminWidgets, w => w.WidgetKey == DashboardWidgetKeys.AuditLogs);
        Assert.DoesNotContain(adminWidgets, w => w.WidgetKey == DashboardWidgetKeys.Vaults);
        Assert.Empty(userWidgets);
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
