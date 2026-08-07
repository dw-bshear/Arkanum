using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arkanum.Tests;

public sealed class Issue022AuditComplianceTests
{
    [Fact]
    public async Task AuditEventsAreAppendOnlyAndCarryVerifiableHashChain()
    {
        await using var db = CreateDb();
        var first = new AuditEventEntity
        {
            Id = "audit-001",
            Type = AuditEventType.LoginSucceeded,
            ActorUserKey = "admin",
            Target = "admin",
            OccurredAt = DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            Message = "Local login succeeded."
        };
        var second = new AuditEventEntity
        {
            Id = "audit-002",
            Type = AuditEventType.SettingsChanged,
            ActorUserKey = "admin",
            Target = "Settings",
            OccurredAt = DateTimeOffset.Parse("2026-08-01T10:01:00Z"),
            Message = "Settings changed. Values were not logged."
        };

        db.AuditEvents.AddRange(first, second);
        await db.SaveChangesAsync();

        Assert.Equal(1, first.Sequence);
        Assert.Equal(2, second.Sequence);
        Assert.Equal(first.IntegrityHash, second.PreviousIntegrityHash);
        Assert.NotEqual(string.Empty, first.IntegrityHash);
        Assert.NotEqual(first.IntegrityHash, second.IntegrityHash);
        Assert.True(AuditEventIntegrity.Verify(first));
        Assert.True(AuditEventIntegrity.Verify(second));

        first.Message = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.Entry(first).State = EntityState.Unchanged;
        db.AuditEvents.Remove(second);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void AuditRetentionPolicyIsDeploymentConfigurableAndDocumentsArchiveExpectations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arkanum:AuditRetentionDays"] = "365"
            })
            .Build();

        var policy = AuditRetentionPolicy.FromConfiguration(configuration);

        Assert.Equal(TimeSpan.FromDays(365), policy.Retention);
        Assert.Equal("365 days", policy.DisplayName);
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));
        Assert.Contains("AuditRetentionPolicy.FromConfiguration", program);
        Assert.DoesNotContain("new AuditRetentionPolicy(TimeSpan.FromDays(90))", program);
        var evidenceGuide = File.ReadAllText(Path.Combine(ProjectRoot(), "docs", "audit-compliance-evidence.md"));
        Assert.Contains("ARKANUM_AUDIT_RETENTION_DAYS", evidenceGuide);
        Assert.Contains("deny UPDATE and DELETE", evidenceGuide);
        Assert.Contains("archive/export/restore", evidenceGuide);
    }

    [Fact]
    public async Task ForwardingFailuresAreTrackedForHealthWithoutSensitiveDetails()
    {
        await using var db = CreateDb();
        await TestPortalSettingsServiceFactory.Create().EnsureDefaultsAsync(db);
        var status = new AuditForwardingStatus();
        status.RecordFailure("audit-secret-id", "tls", new InvalidOperationException("certificate failure for plaintext-secret-value"));
        var service = new ArkanumHealthCheckService(new StaticMasterKeyProvider("issue22-health-master-key-material"), status);

        var result = await service.CheckAsync(db);

        var check = Assert.Single(result.Checks, c => c.Name == "audit-forwarding");
        Assert.Equal("Unhealthy", check.Status);
        Assert.Contains("TLS", check.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit-secret-id", check.Message);
        Assert.DoesNotContain("plaintext-secret-value", check.Message);
    }

    [Theory]
    [InlineData("UDP", AuditForwardingPath.UdpSyslog)]
    [InlineData("TLS", AuditForwardingPath.TlsSyslog)]
    [InlineData("TCP+TLS", AuditForwardingPath.TlsSyslog)]
    [InlineData("WindowsEventLog", AuditForwardingPath.WindowsEventLog)]
    [InlineData("SIEMForwarder", AuditForwardingPath.ApprovedSiemForwarder)]
    public void AuditForwarderRecognizesApprovedForwardingPaths(string protocol, AuditForwardingPath expected)
    {
        var settings = new SyslogSettings("collector.local", "6514", protocol, true);

        Assert.Equal(expected, SyslogAuditForwarder.ResolveForwardingPath(settings));
    }

    [Fact]
    public void AdminSettingsUiAndDocumentationDescribeApprovedForwardingPaths()
    {
        var root = ProjectRoot();
        var settingsMarkup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.Contains("TLS syslog", settingsMarkup);
        Assert.Contains("Windows Event Log", settingsMarkup);
        Assert.Contains("approved SIEM forwarder", settingsMarkup);
        Assert.Contains("hash chain", readme);
        Assert.Contains("database deny UPDATE/DELETE", readme);
        Assert.Contains("audit-compliance-evidence.md", readme);
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
