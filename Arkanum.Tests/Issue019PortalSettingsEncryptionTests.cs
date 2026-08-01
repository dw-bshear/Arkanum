using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Arkanum.Tests;

public class Issue019PortalSettingsEncryptionTests
{
    private static readonly string[] SecretKeys =
    [
        PortalSettingKeys.LdapBindPassword,
        PortalSettingKeys.MailPassword,
        PortalSettingKeys.SqlServerPassword,
        PortalSettingKeys.OidcClientSecret
    ];

    [Fact]
    public async Task SecretValuedSettingsAreEncryptedAtRestAndDecryptedForConsumers()
    {
        await using var db = CreateDb(nameof(SecretValuedSettingsAreEncryptedAtRestAndDecryptedForConsumers));
        var service = Service();

        await service.SaveLdapAsync(db, new LdapSettings("ldap.local", "389", "dc=lab,dc=local", "uid=reader,ou=people,dc=lab,dc=local", "ldap-secret-value", "(objectClass=person)", "uid", "displayName", "mail"), "admin-key");
        await service.SaveMailAsync(db, new MailServerSettings("smtp.local", "587", true, "arkanum@email.com", "Arkanum", "smtp-user"), "mail-secret-value", "admin-key");
        await service.SaveSqlServerAsync(db, "sql.local", "1433", "Arkanum", "arkanum_app", "sql-secret-value", true, false, "admin-key");
        await service.SaveFederationAsync(db, "oidc", "https://login.example.local", "arkanum-web", "oidc-secret-value", "openid profile email", string.Empty, string.Empty, true, "admin-key");

        var rawValues = await db.PortalSettings.Where(s => SecretKeys.Contains(s.SettingKey)).ToDictionaryAsync(s => s.SettingKey, s => s.Value);
        var settings = await service.GetAllAsync(db);

        Assert.All(rawValues, kvp => Assert.StartsWith("arkanum-secret:v1:", kvp.Value));
        Assert.DoesNotContain(rawValues, kvp => kvp.Value.Contains("secret-value", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("ldap-secret-value", settings[PortalSettingKeys.LdapBindPassword]);
        Assert.Equal("mail-secret-value", settings[PortalSettingKeys.MailPassword]);
        Assert.Equal("sql-secret-value", settings[PortalSettingKeys.SqlServerPassword]);
        Assert.Equal("oidc-secret-value", settings[PortalSettingKeys.OidcClientSecret]);
    }

    [Fact]
    public async Task BlankSecretPostsKeepSavedEncryptedValues()
    {
        await using var db = CreateDb(nameof(BlankSecretPostsKeepSavedEncryptedValues));
        var service = Service();

        await service.SaveMailAsync(db, new MailServerSettings("smtp.local", "587", true, "arkanum@email.com", "Arkanum", "smtp-user"), "initial-mail-secret", "admin-key");
        var storedBefore = await RawValueAsync(db, PortalSettingKeys.MailPassword);

        await service.SaveMailAsync(db, new MailServerSettings("smtp2.local", "587", true, "arkanum@email.com", "Arkanum", "smtp-user"), "   ", "admin-key");
        var storedAfter = await RawValueAsync(db, PortalSettingKeys.MailPassword);
        var settings = await service.GetAllAsync(db);

        Assert.Equal(storedBefore, storedAfter);
        Assert.Equal("initial-mail-secret", settings[PortalSettingKeys.MailPassword]);
        Assert.Equal("smtp2.local", settings[PortalSettingKeys.MailSmtpHost]);
    }

    [Fact]
    public async Task ExistingPlaintextSecretSettingsAreProtectedOnRead()
    {
        await using var db = CreateDb(nameof(ExistingPlaintextSecretSettingsAreProtectedOnRead));
        db.PortalSettings.Add(new PortalSettingEntity { SettingKey = PortalSettingKeys.OidcClientSecret, Value = "legacy-plaintext-secret", UpdatedByUserKey = "legacy" });
        await db.SaveChangesAsync();
        var service = Service();

        var settings = await service.GetAllAsync(db);
        var rawStored = await RawValueAsync(db, PortalSettingKeys.OidcClientSecret);

        Assert.Equal("legacy-plaintext-secret", settings[PortalSettingKeys.OidcClientSecret]);
        Assert.StartsWith("arkanum-secret:v1:", rawStored);
        Assert.DoesNotContain("legacy-plaintext-secret", rawStored);
    }

    [Fact]
    public void SettingsUiDoesNotRenderSavedSecretValuesBackToBrowser()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var model = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));

        Assert.DoesNotContain("value=\"@Model.Ldap.BindPassword\"", markup);
        Assert.DoesNotContain("value=\"@Model.MailPassword\"", markup);
        Assert.DoesNotContain("value=\"@Model.SqlServerPassword\"", markup);
        Assert.DoesNotContain("value=\"@Model.OidcClientSecret\"", markup);
        Assert.Contains("PortalSettingsService.ToLdap(settings) with { BindPassword = string.Empty }", model);
    }

    private static PortalSettingsService Service() => new(new SecretEncryptionService(new StaticMasterKeyProvider("issue019-portal-settings-master-key-material")));

    private static async Task<string> RawValueAsync(ArkanumDbContext db, string key) =>
        (await db.PortalSettings.AsNoTracking().SingleAsync(s => s.SettingKey == key)).Value;

    private static ArkanumDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<ArkanumDbContext>()
            .UseInMemoryDatabase(name)
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
