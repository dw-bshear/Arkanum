using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Phase2AccountAuditSyslogTests
{
    [Fact]
    public async Task LocalAuthenticationLocksAccountAfterFailedThresholdAndAuditsLockout()
    {
        await using var db = CreateDb(nameof(LocalAuthenticationLocksAccountAfterFailedThresholdAndAuditsLockout));
        var hasher = new LocalPasswordHasher();
        db.Users.Add(new AppUserEntity
        {
            UserKey = "user-1",
            Username = "lockout-user",
            DisplayName = "Lockout User",
            Source = UserSource.Local,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            PasswordHash = hasher.Hash("CorrectHorse!2026"),
            RolesCsv = "User"
        });
        await db.SaveChangesAsync();
        var auth = new LocalAuthenticationService(hasher);
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < LocalAuthenticationService.LockoutThreshold; i++)
        {
            await auth.AuthenticateAsync(db, "lockout-user", "wrong-password", now.AddSeconds(i));
        }

        var user = await db.Users.SingleAsync(u => u.Username == "lockout-user");
        Assert.Equal(LocalAuthenticationService.LockoutThreshold, user.FailedLoginCount);
        Assert.NotNull(user.LockoutEndAt);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.AccountLocked && e.Target == "lockout-user");

        var lockedResult = await auth.AuthenticateAsync(db, "lockout-user", "CorrectHorse!2026", now.AddMinutes(1));
        Assert.False(lockedResult.Succeeded);
        Assert.Contains("temporarily locked", lockedResult.Message);
    }

    [Fact]
    public async Task SuccessfulLoginAfterLockoutWindowClearsFailureState()
    {
        await using var db = CreateDb(nameof(SuccessfulLoginAfterLockoutWindowClearsFailureState));
        var hasher = new LocalPasswordHasher();
        db.Users.Add(new AppUserEntity
        {
            UserKey = "user-2",
            Username = "expired-lockout-user",
            DisplayName = "Expired Lockout User",
            Source = UserSource.Local,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            PasswordHash = hasher.Hash("CorrectHorse!2026"),
            RolesCsv = "User",
            FailedLoginCount = LocalAuthenticationService.LockoutThreshold,
            LockoutEndAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        await db.SaveChangesAsync();
        var auth = new LocalAuthenticationService(hasher);

        var result = await auth.AuthenticateAsync(db, "expired-lockout-user", "CorrectHorse!2026", DateTimeOffset.UtcNow);

        Assert.True(result.Succeeded);
        var user = await db.Users.SingleAsync(u => u.Username == "expired-lockout-user");
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEndAt);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.LoginSucceeded && e.Target == "expired-lockout-user");
    }

    [Fact]
    public async Task SettingsChangesCreateAuditEventWithoutLoggingValues()
    {
        await using var db = CreateDb(nameof(SettingsChangesCreateAuditEventWithoutLoggingValues));
        var settings = new PortalSettingsService();
        await settings.EnsureDefaultsAsync(db);

        await settings.SaveSyslogAsync(db, new SyslogSettings("syslog.internal", "514", "UDP", true), "admin-key");

        var audit = await db.AuditEvents.SingleAsync(e => e.Type == AuditEventType.SettingsChanged);
        Assert.Equal("admin-key", audit.ActorUserKey);
        Assert.Contains(PortalSettingKeys.SyslogHost, audit.Message);
        Assert.Contains("Values were not logged", audit.Message);
        Assert.DoesNotContain("syslog.internal", audit.Message);
    }

    [Fact]
    public void SyslogFormatterUsesStructuredAuditMetadataWithoutNewlines()
    {
        var audit = new AuditEventEntity
        {
            Id = "audit-1",
            Type = AuditEventType.SecretReveal,
            ActorUserKey = "actor\nkey",
            Target = "Firewall \"Admin\"",
            OccurredAt = DateTimeOffset.Parse("2026-06-28T18:00:00Z"),
            Message = "Secret revealed\nwithout plaintext."
        };

        var message = SyslogAuditForwarder.FormatMessage(audit);

        Assert.Contains("type=\"SecretReveal\"", message);
        Assert.Contains("actor=\"actor key\"", message);
        Assert.Contains("target=\"Firewall 'Admin'\"", message);
        Assert.DoesNotContain("\n", message);
        Assert.DoesNotContain("\r", message);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }
}
