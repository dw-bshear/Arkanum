using Arkanum.Web.Domain;
using Arkanum.Web.Services;

namespace Arkanum.Tests;

public class ArkanumDomainTests
{
    [Fact]
    public void Imported_directory_users_are_created_disabled_with_user_role()
    {
        var service = new DirectorySyncService();
        var result = service.SyncUsers(
            [],
            [new DirectoryUserRecord("S-1-5-21-1000", "jdoe", "Jane Doe", "jane@example.com")],
            DateTimeOffset.Parse("2026-06-26T12:00:00Z"));

        var user = Assert.Single(result.Users);
        Assert.Equal(UserSource.Directory, user.Source);
        Assert.True(user.DirectoryPresent);
        Assert.False(user.AppAccessEnabled);
        Assert.Contains(ArkanumRole.User, user.Roles);
        Assert.Equal("Pending administrator activation", user.DisabledReason);
    }

    [Fact]
    public void Missing_directory_users_are_disabled_not_deleted()
    {
        var existing = new ArkanumUser
        {
            UserKey = "user-1",
            ExternalId = "S-1-5-21-1000",
            Username = "jdoe",
            DisplayName = "Jane Doe",
            Email = "jane@example.com",
            Source = UserSource.Directory,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            Roles = [ArkanumRole.User, ArkanumRole.VaultManager]
        };

        var service = new DirectorySyncService();
        var result = service.SyncUsers([existing], [], DateTimeOffset.Parse("2026-06-27T12:00:00Z"));

        var user = Assert.Single(result.Users);
        Assert.False(user.DirectoryPresent);
        Assert.False(user.AppAccessEnabled);
        Assert.Equal("Not found in latest directory sync", user.DisabledReason);
        Assert.Contains(ArkanumRole.VaultManager, user.Roles);
    }

    [Fact]
    public void Reappearing_directory_users_require_admin_reactivation()
    {
        var existing = new ArkanumUser
        {
            UserKey = "user-1",
            ExternalId = "S-1-5-21-1000",
            Username = "jdoe",
            Source = UserSource.Directory,
            DirectoryPresent = false,
            AppAccessEnabled = false,
            DisabledReason = "Not found in latest directory sync",
            Roles = [ArkanumRole.User]
        };

        var service = new DirectorySyncService();
        var result = service.SyncUsers(
            [existing],
            [new DirectoryUserRecord("S-1-5-21-1000", "jdoe", "Jane Doe", "jane@example.com")],
            DateTimeOffset.Parse("2026-06-28T12:00:00Z"));

        var user = Assert.Single(result.Users);
        Assert.True(user.DirectoryPresent);
        Assert.False(user.AppAccessEnabled);
        Assert.Equal("Returned after absence; requires administrator reactivation", user.DisabledReason);
    }

    [Fact]
    public void Password_generator_obeys_admin_policy_and_uses_each_required_class()
    {
        var policy = PasswordGeneratorPolicy.Default with { DefaultLength = 32, MinimumLength = 20 };
        var generator = new StrongPasswordGenerator();

        var password = generator.Generate(policy);

        Assert.Equal(32, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => policy.SymbolSet.Contains(c));
    }

    [Fact]
    public void Audit_retention_prunes_events_older_than_ninety_days()
    {
        var retention = new AuditRetentionPolicy(TimeSpan.FromDays(90));
        var now = DateTimeOffset.Parse("2026-06-26T12:00:00Z");
        var events = new[]
        {
            new AuditEvent("old", AuditEventType.SecretReveal, "u1", now.AddDays(-91)),
            new AuditEvent("fresh", AuditEventType.SecretCopy, "u2", now.AddDays(-90)),
            new AuditEvent("new", AuditEventType.UserEnabled, "admin", now.AddDays(-1))
        };

        var retained = retention.FilterRetained(events, now).Select(e => e.Id).ToArray();

        Assert.DoesNotContain("old", retained);
        Assert.Contains("fresh", retained);
        Assert.Contains("new", retained);
    }

    [Fact]
    public void First_admin_setup_is_available_only_until_an_admin_exists()
    {
        var setup = new FirstRunSetupService();
        Assert.True(setup.IsSetupRequired([]));
        Assert.False(setup.IsSetupRequired([new ArkanumUser { Username = "admin", Source = UserSource.Local, Roles = [ArkanumRole.Administrator] }]));
    }
}
