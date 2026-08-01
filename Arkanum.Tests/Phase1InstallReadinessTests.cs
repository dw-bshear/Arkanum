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
        await TestPortalSettingsServiceFactory.Create().EnsureDefaultsAsync(db);
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
        await TestPortalSettingsServiceFactory.Create().EnsureDefaultsAsync(db);
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

    [Fact]
    public void Release_package_includes_gui_installer_entry_points_for_windows_and_linux()
    {
        var root = ProjectRoot();
        var linuxGui = Path.Combine(root, "installers", "linux", "arkanum-gui-install.py");
        var windowsGui = Path.Combine(root, "installers", "windows", "Install-ArkanumGui.ps1");
        var installerGuide = Path.Combine(root, "docs", "gui-installer-guide.md");

        Assert.True(File.Exists(linuxGui), "Linux GUI installer entry point is missing.");
        Assert.True(File.Exists(windowsGui), "Windows GUI installer entry point is missing.");
        Assert.True(File.Exists(installerGuide), "GUI installer guide is missing.");

        var linux = File.ReadAllText(linuxGui);
        var windows = File.ReadAllText(windowsGui);
        var guide = File.ReadAllText(installerGuide);

        Assert.Contains("SQL Server", linux);
        Assert.Contains("Recovery Key", linux);
        Assert.Contains("SQL Server", windows);
        Assert.Contains("Recovery Key", windows);
        Assert.Contains("Windows installation package", guide);
        Assert.Contains("Linux installation package", guide);
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
