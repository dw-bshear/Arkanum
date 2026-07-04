using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Arkanum.Tests;

public class Phase3ReleaseEvidenceTests
{
    [Fact]
    public async Task InstallReportSummarizesHealthCountsAndSecurityControlsWithoutSecrets()
    {
        await using var db = CreateDb(nameof(InstallReportSummarizesHealthCountsAndSecurityControlsWithoutSecrets));
        await db.Database.EnsureCreatedAsync();
        await new PortalSettingsService().EnsureDefaultsAsync(db);
        db.Users.Add(new AppUserEntity { Username = "admin", DisplayName = "Admin", Source = UserSource.Local, DirectoryPresent = true, AppAccessEnabled = true, PasswordHash = "hash", RolesCsv = "Administrator,User" });
        db.Vaults.Add(new VaultEntity { Name = "Ops", Description = "Ops vault", CreatedByUserKey = "admin", CreatedAt = DateTimeOffset.UtcNow });
        db.AuditEvents.Add(new AuditEventEntity { Type = AuditEventType.LoginSucceeded, ActorUserKey = "admin", Target = "admin", OccurredAt = DateTimeOffset.UtcNow, Message = "Local login succeeded." });
        await db.SaveChangesAsync();
        var service = new InstallReportService(
            new ArkanumHealthCheckService(new StaticMasterKeyProvider("phase3-install-report-master-key-material")),
            new AuditRetentionPolicy(TimeSpan.FromDays(90)),
            new FakeEnvironment(),
            new ConfigurationBuilder().Build());

        var report = await service.BuildAsync(db);

        Assert.Equal("Arkanum", report.Product);
        Assert.Equal("phase4-preview", report.Version);
        Assert.Equal("Healthy", report.HealthStatus);
        Assert.Equal(1, report.Counts.Users);
        Assert.True(report.SecurityControls.BrowserSecurityHeadersEnabled);
        Assert.True(report.SecurityControls.SensitivePageNoStoreEnabled);
        Assert.Equal("Strict", report.SecurityControls.AuthCookieSameSite);
        Assert.Equal(LocalAuthenticationService.LockoutThreshold, report.SecurityControls.LocalAccountLockoutThreshold);
        Assert.Equal("90 days", report.OperationalSettings.AuditRetention);
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);
        Assert.DoesNotContain("phase3-install-report-master-key-material", serialized);
        Assert.DoesNotContain("CorrectHorse!2026", serialized);
        Assert.DoesNotContain("\"passwordHash\"", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Arkanum.Tests";
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
