using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class FirstIterationTests
{
    [Fact]
    public void Local_password_hash_verifies_correct_password_and_rejects_wrong_password()
    {
        var hasher = new LocalPasswordHasher();
        var hash = hasher.Hash("ChangeMeNow!23456789");

        Assert.True(hasher.Verify("ChangeMeNow!23456789", hash));
        Assert.False(hasher.Verify("wrong-password", hash));
        Assert.DoesNotContain("ChangeMeNow", hash);
    }

    [Fact]
    public async Task First_run_setup_creates_enabled_local_admin_once()
    {
        await using var db = CreateDb();
        var service = new FirstRunSetupService();
        var hasher = new LocalPasswordHasher();

        var created = await service.CreateInitialAdministratorAsync(db, hasher, "initial-admin", "ChangeMeNow!23456789", DateTimeOffset.Parse("2026-06-26T12:00:00Z"));

        Assert.True(created.AppAccessEnabled);
        Assert.Equal(UserSource.Local, created.Source);
        Assert.Contains(ArkanumRole.Administrator, created.Roles);
        Assert.False(service.IsSetupRequired(await service.LoadUsersAsync(db)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateInitialAdministratorAsync(db, hasher, "second-admin", "ChangeMeNow!23456789", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Local_authentication_records_success_and_failure_without_logging_passwords()
    {
        await using var db = CreateDb();
        var setup = new FirstRunSetupService();
        var hasher = new LocalPasswordHasher();
        await setup.CreateInitialAdministratorAsync(db, hasher, "initial-admin", "ChangeMeNow!23456789", DateTimeOffset.Parse("2026-06-26T12:00:00Z"));
        var auth = new LocalAuthenticationService(hasher);

        var success = await auth.AuthenticateAsync(db, "initial-admin", "ChangeMeNow!23456789", DateTimeOffset.Parse("2026-06-26T12:05:00Z"));
        var failure = await auth.AuthenticateAsync(db, "initial-admin", "bad-password", DateTimeOffset.Parse("2026-06-26T12:06:00Z"));

        Assert.True(success.Succeeded);
        Assert.False(failure.Succeeded);
        var auditText = string.Join('\n', await db.AuditEvents.Select(e => e.Message).ToListAsync());
        Assert.Contains("Local login succeeded", auditText);
        Assert.Contains("Local login failed", auditText);
        Assert.DoesNotContain("ChangeMeNow", auditText);
        Assert.DoesNotContain("bad-password", auditText);
    }

    [Fact]
    public void Auditor_can_view_audit_logs_but_not_reveal_or_edit_without_another_role()
    {
        var authorization = new VaultAuthorizationService();
        var auditor = new ArkanumUser { Roles = [ArkanumRole.User, ArkanumRole.Auditor] };
        var manager = new ArkanumUser { Roles = [ArkanumRole.User, ArkanumRole.VaultManager] };

        Assert.False(authorization.CanRevealAnySecret(auditor));
        Assert.True(authorization.CanExportAuditLogs(auditor));
        Assert.False(authorization.CanEditVault(auditor, assignedVaultManager: false));
        Assert.True(authorization.CanEditVault(manager, assignedVaultManager: true));
    }

    [Fact]
    public async Task Vault_repository_creates_vault_with_default_folder_and_audit_event()
    {
        await using var db = CreateDb();
        var repository = new VaultRepository();

        var vault = await repository.CreateVaultAsync(db, "Operations", "Shared operational credentials", "admin-key", DateTimeOffset.Parse("2026-06-26T12:10:00Z"));

        Assert.Equal("Operations", vault.Name);
        Assert.Equal("General", Assert.Single(await db.VaultFolders.Where(f => f.VaultId == vault.Id).ToListAsync()).Name);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.VaultAssignmentChanged && e.Target == "Operations");
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
