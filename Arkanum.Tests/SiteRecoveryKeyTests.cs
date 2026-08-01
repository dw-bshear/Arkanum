using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class SiteRecoveryKeyTests
{
    [Fact]
    public void Generates_site_wide_recovery_key_with_expected_prefix_and_entropy()
    {
        var key = SiteRecoveryKeyService.GenerateRecoveryKey();

        Assert.StartsWith("ark-recovery-v1.", key);
        Assert.True(SiteRecoveryKeyService.TryValidateRecoveryKeyFormat(key, out var error), error);
        Assert.Null(error);
        Assert.NotEqual(key, SiteRecoveryKeyService.GenerateRecoveryKey());
    }

    [Fact]
    public async Task Recovery_sentinel_validates_same_site_key_and_rejects_wrong_key()
    {
        await using var db = CreateDb(nameof(Recovery_sentinel_validates_same_site_key_and_rejects_wrong_key));
        await db.Database.EnsureCreatedAsync();
        var correctKey = "ark-recovery-v1.correct-site-key-material-for-tests";
        var wrongKey = "ark-recovery-v1.wrong-site-key-material-for-tests";

        var service = new SiteRecoveryKeyService(new StaticMasterKeyProvider(correctKey));
        await service.EnsureRecoverySentinelAsync(db, DateTimeOffset.Parse("2026-07-03T12:00:00Z"));

        var valid = await service.ValidateRecoverySentinelAsync(db);
        var invalid = await new SiteRecoveryKeyService(new StaticMasterKeyProvider(wrongKey)).ValidateRecoverySentinelAsync(db);

        Assert.True(valid.IsValid, valid.Message);
        Assert.False(invalid.IsValid);
        Assert.Contains("does not match", invalid.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.RecoveryStates.ToListAsync());
    }

    [Fact]
    public async Task Health_check_reports_database_key_match_using_site_recovery_sentinel()
    {
        await using var db = CreateDb(nameof(Health_check_reports_database_key_match_using_site_recovery_sentinel));
        await db.Database.EnsureCreatedAsync();
        await TestPortalSettingsServiceFactory.Create().EnsureDefaultsAsync(db);
        var key = "ark-recovery-v1.health-site-key-material-for-tests";
        await new SiteRecoveryKeyService(new StaticMasterKeyProvider(key)).EnsureRecoverySentinelAsync(db, DateTimeOffset.UtcNow);

        var healthy = await new ArkanumHealthCheckService(new StaticMasterKeyProvider(key)).CheckAsync(db);
        var unhealthy = await new ArkanumHealthCheckService(new StaticMasterKeyProvider("ark-recovery-v1.different-key-material-for-tests")).CheckAsync(db);

        Assert.Contains(healthy.Checks, check => check.Name == "recovery-key" && check.Status == "Healthy");
        Assert.False(unhealthy.IsHealthy);
        Assert.Contains(unhealthy.Checks, check => check.Name == "recovery-key" && check.Status == "Unhealthy");
    }


    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }
}
