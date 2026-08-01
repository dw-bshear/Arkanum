using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Arkanum.Tests;

public sealed class Issue021ProductionTlsPostureTests
{
    [Fact]
    public void ProductionCookiesAndOidcTransientCookiesDefaultSecureAlways()
    {
        var options = new CookieAuthenticationOptions();
        var configuration = new ConfigurationBuilder().Build();

        ArkanumCookiePolicy.Configure(options, configuration, "Production");

        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, ArkanumCookiePolicy.ResolveSecurePolicy(null, "Production"));
        Assert.Equal(CookieSecurePolicy.Always, ArkanumCookiePolicy.ResolveSecurePolicy("None", "Production"));
        Assert.Contains("CookieSecurePolicy.Always", File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Services", "FederatedAuthenticationService.cs")));
    }

    [Fact]
    public void DevelopmentAndTestingCanExplicitlyDowngradeCookieSecurePolicy()
    {
        Assert.Equal(CookieSecurePolicy.SameAsRequest, ArkanumCookiePolicy.ResolveSecurePolicy("SameAsRequest", "Development"));
        Assert.Equal(CookieSecurePolicy.None, ArkanumCookiePolicy.ResolveSecurePolicy("None", "Testing"));
        Assert.Equal(CookieSecurePolicy.Always, ArkanumCookiePolicy.ResolveSecurePolicy("SameAsRequest", "Production"));
    }

    [Fact]
    public void RuntimePipelineEnablesHttpsRedirectionAndHstsOutsideDevelopment()
    {
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));

        Assert.Contains("UseHttpsRedirection", program);
        Assert.Contains("UseHsts", program);
        Assert.Contains("!app.Environment.IsDevelopment()", program);
    }

    [Fact]
    public async Task PortalDefaultsRequireValidatedSqlTlsAndSecureLdap()
    {
        await using var db = CreateDb();
        var service = TestPortalSettingsServiceFactory.Create();
        var settings = await service.GetAllAsync(db);

        var sql = PortalSettingsService.ToSqlServer(settings);
        var ldap = PortalSettingsService.ToLdap(settings);

        Assert.True(sql.Encrypt);
        Assert.False(sql.TrustServerCertificate);
        Assert.Equal("636", ldap.Port);
        Assert.Equal("LDAPS", ldap.SecurityMode);
        Assert.True(ldap.VerifyServerCertificate);
        Assert.False(ldap.AllowInsecureForDevelopment);
    }

    [Fact]
    public void LdapRuntimeSupportsLdapsStartTlsCertificateValidationAndExplicitInsecureDevMode()
    {
        var service = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Services", "DirectoryLdapImportService.cs"));
        var settingsMarkup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));

        Assert.Contains("SecureSocketLayer", service);
        Assert.Contains("StartTransportLayerSecurity", service);
        Assert.Contains("VerifyServerCertificate", service);
        Assert.DoesNotContain("certificate is not null", service);
        Assert.Contains("IWebHostEnvironment", service);
        Assert.Contains("IsDevelopmentOrTesting", service);
        Assert.Contains("Disabling LDAP server certificate validation is blocked", service);
        Assert.Contains("Allow insecure LDAP for development/testing only", settingsMarkup);
        Assert.Contains("Verify LDAP server certificate", settingsMarkup);
        Assert.Contains("value=\"LDAPS\"", settingsMarkup);
        Assert.Contains("value=\"StartTLS\"", settingsMarkup);
    }

    [Fact]
    public void InstallersAndDocsRequireHttpsAndValidatedSqlCertificatesByDefault()
    {
        var root = ProjectRoot();
        var windowsInstaller = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-Arkanum.ps1"));
        var windowsGui = File.ReadAllText(Path.Combine(root, "installers", "windows", "Install-ArkanumGui.ps1"));
        var windowsTest = File.ReadAllText(Path.Combine(root, "installers", "windows", "Test-ArkanumInstall.ps1"));
        var linuxInstaller = File.ReadAllText(Path.Combine(root, "installers", "linux", "install-arkanum.sh"));
        var linuxGui = File.ReadAllText(Path.Combine(root, "installers", "linux", "arkanum-gui-install.py"));
        var guide = File.ReadAllText(Path.Combine(root, "docs", "gui-installer-guide.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var linuxManual = File.ReadAllText(Path.Combine(root, "docs", "linux-manual-install-package.md"));
        var backupRestore = File.ReadAllText(Path.Combine(root, "docs", "backup-and-restore.md"));

        Assert.Contains("TrustServerCertificate=False", windowsInstaller);
        Assert.Contains("TrustServerCertificate=False", linuxInstaller);
        Assert.Contains("self.trust = tk.BooleanVar(value=False)", linuxGui);
        Assert.Contains("$trust.Checked = $false", windowsGui);
        Assert.Contains("-HttpsCertificateThumbprint", windowsGui);
        Assert.Contains("ARKANUM_HTTPS_CERTIFICATE_PATH", linuxInstaller);
        Assert.Contains("ASPNETCORE_Kestrel__Certificates__Default__Path", linuxInstaller);
        Assert.Contains("https://$($script:HealthHost):$Port/health", windowsInstaller);
        Assert.Contains("https://localhost:443/health", windowsTest);
        Assert.Contains("New-WebBinding", windowsInstaller);
        Assert.Contains("-Protocol 'https'", windowsInstaller);
        Assert.Contains("Remove-WebBinding", windowsInstaller);
        Assert.DoesNotContain("New-Website -Name $SiteName -Port $Port -Protocol https", windowsInstaller);
        Assert.Contains("HttpsCertificateThumbprint is required", windowsInstaller);
        Assert.Contains("$script:HealthHost", windowsInstaller);
        Assert.Contains("sslFlags", windowsInstaller);
        Assert.Contains("HTTPS binding", guide);
        Assert.Contains("TLS 1.2/1.3", guide);
        Assert.DoesNotContain("TrustServerCertificate=True;", guide);
        Assert.DoesNotContain("TrustServerCertificate=True;", readme);
        Assert.DoesNotContain("TrustServerCertificate=True;", linuxManual);
        Assert.DoesNotContain("TrustServerCertificate=True;", backupRestore);
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
