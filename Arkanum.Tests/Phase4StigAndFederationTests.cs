using System.Security.Claims;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Arkanum.Tests;

public class Phase4StigAndFederationTests
{
    [Fact]
    public void ComplianceCatalogCoversPriorityFamiliesWithoutClaimingEverythingIsImplemented()
    {
        Assert.Contains(ComplianceControlCatalog.Controls, c => c.Family.Contains("Identification") && c.Status == ComplianceControlStatus.Partial);
        Assert.Contains(ComplianceControlCatalog.Controls, c => c.Family == "Access Control" && c.Status == ComplianceControlStatus.Implemented);
        Assert.Contains(ComplianceControlCatalog.Controls, c => c.Family.Contains("Audit") && c.Status == ComplianceControlStatus.Implemented);
        Assert.Contains(ComplianceControlCatalog.Controls, c => c.Family.Contains("Configuration") && c.Status == ComplianceControlStatus.Partial);
        Assert.Contains(ComplianceControlCatalog.Controls, c => c.Status == ComplianceControlStatus.Planned);
    }

    [Fact]
    public async Task FederationSettingsPersistOidcReadinessWithoutLoggingClientSecret()
    {
        await using var db = CreateDb(nameof(FederationSettingsPersistOidcReadinessWithoutLoggingClientSecret));
        var service = TestPortalSettingsServiceFactory.Create();
        await service.EnsureDefaultsAsync(db);

        await service.SaveFederationAsync(db, "oidc", "https://login.example.local/realms/arkanum", "arkanum-web", "super-secret-client-value", "openid profile email groups", string.Empty, string.Empty, true, "admin-key");
        var settings = await service.GetAllAsync(db);
        var federation = PortalSettingsService.ToFederation(settings);

        Assert.Equal("OIDC", federation.Mode);
        Assert.True(federation.OidcConfigured);
        Assert.True(federation.HasOidcClientSecret);
        Assert.True(federation.RequireExternalMfa);
        var auditText = string.Join('\n', await db.AuditEvents.Select(e => e.Message).ToListAsync());
        Assert.Contains(PortalSettingKeys.OidcClientSecret, auditText);
        Assert.DoesNotContain("super-secret-client-value", auditText);
    }

    [Fact]
    public async Task FederationSettingsValidateSamlReadinessShape()
    {
        await using var db = CreateDb(nameof(FederationSettingsValidateSamlReadinessShape));
        var service = TestPortalSettingsServiceFactory.Create();
        await service.EnsureDefaultsAsync(db);

        await service.SaveFederationAsync(db, "SAML", string.Empty, string.Empty, null, string.Empty, "https://idp.example.local/metadata.xml", "https://arkanum.example.local/saml", true, "admin-key");
        var federation = PortalSettingsService.ToFederation(await service.GetAllAsync(db));

        Assert.Equal("SAML", federation.Mode);
        Assert.True(federation.SamlConfigured);
        Assert.False(federation.OidcConfigured);
    }

    [Fact]
    public async Task InstallReportIncludesComplianceMappingsAndFederationReadinessWithoutSecrets()
    {
        await using var db = CreateDb(nameof(InstallReportIncludesComplianceMappingsAndFederationReadinessWithoutSecrets));
        var settingsService = TestPortalSettingsServiceFactory.Create();
        await settingsService.EnsureDefaultsAsync(db);
        await settingsService.SaveFederationAsync(db, "OIDC", "https://login.example.local", "arkanum-web", "do-not-report-this-secret", "openid profile email", string.Empty, string.Empty, true, "admin-key");
        var reportService = new InstallReportService(
            new ArkanumHealthCheckService(new StaticMasterKeyProvider("phase4-install-report-master-key-material")),
            new AuditRetentionPolicy(TimeSpan.FromDays(90)),
            new FakeEnvironment(),
            new ConfigurationBuilder().Build());

        var report = await reportService.BuildAsync(db);
        var serialized = System.Text.Json.JsonSerializer.Serialize(report);

        Assert.Equal("OIDC", report.FederationReadiness.Mode);
        Assert.True(report.FederationReadiness.OidcConfigured);
        Assert.False(report.FederationReadiness.SmartCardDirectIntegrationRequired);
        Assert.Contains(report.ComplianceMappings, c => c.Id == "APP-AUTH-001");
        Assert.Contains(report.ComplianceMappings, c => c.Id == "APP-AU-001");
        Assert.DoesNotContain("do-not-report-this-secret", serialized);
        Assert.DoesNotContain("phase4-install-report-master-key-material", serialized);
    }

    [Fact]
    public void SettingsMarkupIncludesFederationTabAndSmartCardAsIdpPolicy()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));

        Assert.Contains("SAML / OIDC", markup);
        Assert.Contains("Federated identity readiness", markup);
        Assert.Contains("Smart card/PIV can be satisfied upstream by the IdP", markup);
        Assert.Contains("asp-page-handler=\"Federation\"", markup);
    }

    [Fact]
    public async Task FederatedOidcPrincipalMapsOnlyToActiveNonLocalArkanumUsers()
    {
        await using var db = CreateDb(nameof(FederatedOidcPrincipalMapsOnlyToActiveNonLocalArkanumUsers));
        var service = new FederatedAuthenticationService(TestPortalSettingsServiceFactory.Create());
        db.Users.Add(new AppUserEntity
        {
            ExternalId = "oidc-subject-1",
            Username = "jane.doe",
            DisplayName = "Jane Doe",
            Email = "jane.doe@example.com",
            Source = UserSource.Directory,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            RolesCsv = string.Join(',', ArkanumRole.User, ArkanumRole.VaultManager)
        });
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "oidc-subject-1"),
            new Claim("preferred_username", "jane.doe"),
            new Claim(ClaimTypes.Email, "jane.doe@example.com")
        }, "oidc-test"));

        var user = await service.ResolveFederatedPrincipalAsync(db, principal, DateTimeOffset.Parse("2026-06-28T12:00:00Z"));
        var appPrincipal = FederatedAuthenticationService.BuildApplicationPrincipal(user!);

        Assert.NotNull(user);
        Assert.Equal("jane.doe", user!.Username);
        Assert.Contains(user.Roles, r => r == ArkanumRole.VaultManager);
        Assert.Equal("jane.doe", appPrincipal.Identity?.Name);
        Assert.True(appPrincipal.IsInRole(nameof(ArkanumRole.VaultManager)));
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.LoginSucceeded && e.Message.Contains("Federated OIDC"));
    }

    [Fact]
    public async Task FederatedOidcPrincipalCannotImpersonateLocalEmergencyAdmin()
    {
        await using var db = CreateDb(nameof(FederatedOidcPrincipalCannotImpersonateLocalEmergencyAdmin));
        var service = new FederatedAuthenticationService(TestPortalSettingsServiceFactory.Create());
        db.Users.Add(new AppUserEntity
        {
            ExternalId = "local-admin-subject",
            Username = "admin",
            DisplayName = "Local Admin",
            Email = "admin@example.com",
            Source = UserSource.Local,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            RolesCsv = string.Join(',', ArkanumRole.Administrator, ArkanumRole.User),
            PasswordHash = "local-password-hash"
        });
        await db.SaveChangesAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "local-admin-subject"),
            new Claim("preferred_username", "admin"),
            new Claim(ClaimTypes.Email, "admin@example.com")
        }, "oidc-test"));

        var user = await service.ResolveFederatedPrincipalAsync(db, principal, DateTimeOffset.Parse("2026-06-28T12:05:00Z"));

        Assert.Null(user);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == AuditEventType.LoginFailed && e.Message.Contains("not an active Arkanum user"));
    }

    [Fact]
    public void LoginMarkupSeparatesOidcAndManualEmergencyPages()
    {
        var loginMarkup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Login.cshtml"));
        var loginPageModel = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Login.cshtml.cs"));
        var manualLoginMarkup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "ManualLogin.cshtml"));
        var manualLoginPageModel = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "ManualLogin.cshtml.cs"));

        Assert.Contains("asp-page-handler=\"Oidc\"", loginMarkup);
        Assert.Contains("Sign in with OIDC", loginMarkup);
        Assert.Contains("If you were not automatically logged into Arkanum, use the link below to sign in manually with your username and password.", loginMarkup);
        Assert.Contains("asp-page=\"/ManualLogin\"", loginMarkup);
        Assert.DoesNotContain("Use OIDC federation when enabled by an administrator", loginMarkup);
        Assert.DoesNotContain("Local emergency access is available on a separate manual sign-in page", loginMarkup);
        Assert.DoesNotContain("Continue to the configured OIDC identity provider", loginMarkup);
        Assert.DoesNotContain("Smart card/PIV/CAC policy", loginMarkup);
        Assert.DoesNotContain("Need emergency local access?", loginMarkup);
        Assert.DoesNotContain("asp-for=\"Username\"", loginMarkup);
        Assert.DoesNotContain("asp-for=\"Password\"", loginMarkup);
        Assert.Contains("Challenge(new AuthenticationProperties", loginPageModel);
        Assert.Contains("FederatedAuthenticationService.OidcScheme", loginPageModel);

        Assert.Contains("Emergency Access", manualLoginMarkup);
        Assert.Contains("This page provides a manual login point when Single Sign-On is not functioning.", manualLoginMarkup);
        Assert.DoesNotContain("Local emergency access", manualLoginMarkup);
        Assert.DoesNotContain("Use this page only for local administrator or emergency accounts when federated sign-in is unavailable.", manualLoginMarkup);
        Assert.Contains("asp-for=\"Username\"", manualLoginMarkup);
        Assert.Contains("asp-for=\"Password\"", manualLoginMarkup);
        Assert.Contains("asp-page=\"/Login\"", manualLoginMarkup);
        Assert.Contains("LocalAuthenticationService", manualLoginPageModel);
        Assert.DoesNotContain("asp-page-handler=\"Oidc\"", manualLoginMarkup);
    }

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new ArkanumDbContext(options);
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
