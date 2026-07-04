using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Tests;

public class Phase1InstallReadinessTests
{
    [Fact]
    public async Task HealthCheckReportsHealthyWhenDatabaseDefaultsAndMasterKeyAreAvailable()
    {
        await using var db = CreateDb(nameof(HealthCheckReportsHealthyWhenDatabaseDefaultsAndMasterKeyAreAvailable));
        await db.Database.EnsureCreatedAsync();
        await new PortalSettingsService().EnsureDefaultsAsync(db);
        var service = new ArkanumHealthCheckService(new StaticMasterKeyProvider("phase1-health-test-master-key-material"));

        var result = await service.CheckAsync(db);

        Assert.True(result.IsHealthy);
        Assert.Contains(result.Checks, check => check.Name == "database" && check.Status == "Healthy");
        Assert.Contains(result.Checks, check => check.Name == "settings" && check.Status == "Healthy");
        Assert.Contains(result.Checks, check => check.Name == "encryption-key" && check.Status == "Healthy");
        Assert.Equal(ArkanumHealthCheckService.CurrentSchemaVersion, result.Version);
    }

    [Fact]
    public async Task HealthCheckReportsUnhealthyWhenMasterKeyIsMissing()
    {
        await using var db = CreateDb(nameof(HealthCheckReportsUnhealthyWhenMasterKeyIsMissing));
        await db.Database.EnsureCreatedAsync();
        await new PortalSettingsService().EnsureDefaultsAsync(db);
        var service = new ArkanumHealthCheckService(new MissingMasterKeyProvider());

        var result = await service.CheckAsync(db);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.Checks, check => check.Name == "encryption-key" && check.Status == "Unhealthy");
    }

    [Fact]
    public async Task HealthCheckReportsUnhealthyWhenRequiredSettingsAreMissing()
    {
        await using var db = CreateDb(nameof(HealthCheckReportsUnhealthyWhenRequiredSettingsAreMissing));
        await db.Database.EnsureCreatedAsync();
        var service = new ArkanumHealthCheckService(new StaticMasterKeyProvider("phase1-health-test-master-key-material"));

        var result = await service.CheckAsync(db);

        Assert.False(result.IsHealthy);
        Assert.Contains(result.Checks, check => check.Name == "settings" && check.Status == "Unhealthy");
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }

    private sealed class MissingMasterKeyProvider : IMasterKeyProvider
    {
        public string KeyId => "missing";
        public byte[] GetMasterKey() => throw new InvalidOperationException("Missing test key.");
    }
}
