using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Arkanum.Tests;

public class Iteration008SettingsTests
{
    [Fact]
    public async Task Portal_settings_defaults_and_dashboard_text_can_be_saved()
    {
        await using var db = CreateDb();
        var service = new PortalSettingsService();

        var defaults = await service.GetDashboardTextAsync(db);
        await service.SaveDashboardTextAsync(db, "Custom vault headline", "Custom vault description", "admin-key");
        var saved = await service.GetDashboardTextAsync(db);

        Assert.Equal("Your starting point for vault work.", defaults.HeroTitle);
        Assert.Equal("Custom vault headline", saved.HeroTitle);
        Assert.Equal("Custom vault description", saved.HeroDescription);
        Assert.Contains(await db.PortalSettings.ToListAsync(), s => s.SettingKey == PortalSettingKeys.DashboardHeroTitle && s.UpdatedByUserKey == "admin-key");
    }

    [Fact]
    public async Task Ldap_mail_and_syslog_settings_are_persisted()
    {
        await using var db = CreateDb();
        var service = new PortalSettingsService();

        await service.SaveLdapAsync(db, new LdapSettings("ldap.local", "389", "dc=lab,dc=local", "uid=reader,ou=people,dc=lab,dc=local", "reader-password", "(objectClass=person)", "uid", "displayName", "mail"), "admin-key");
        await service.SaveMailAsync(db, new MailServerSettings("smtp.local", "587", true, "arkanum@email.com", "Arkanum"), "admin-key");
        await service.SaveNotificationTemplatesAsync(db, new NotificationTemplateSettings("Notice", "Hello {{displayName}}", "Vault {{vaultName}}", "Access to {{vaultName}}"), "admin-key");
        await service.SaveSyslogAsync(db, new SyslogSettings("syslog.local", "514", "UDP", true), "admin-key");
        var all = await service.GetAllAsync(db);

        Assert.Equal("ldap.local", all[PortalSettingKeys.LdapHost]);
        Assert.Equal("reader-password", all[PortalSettingKeys.LdapBindPassword]);
        Assert.Equal("smtp.local", all[PortalSettingKeys.MailSmtpHost]);
        Assert.Equal("Vault {{vaultName}}", all[PortalSettingKeys.NotificationVaultAccessSubject]);
        Assert.Equal("syslog.local", all[PortalSettingKeys.SyslogHost]);
        Assert.Equal("true", all[PortalSettingKeys.SyslogEnabled]);
    }

    [Fact]
    public async Task Admin_user_service_can_toggle_access_and_replace_roles()
    {
        await using var db = CreateDb();
        var service = new AdminUserService(new LocalPasswordHasher());
        var user = await service.CreateLocalUserAsync(db, "role-target", "Password-12345!", [ArkanumRole.User], "admin-key", DateTimeOffset.UtcNow);

        await service.SetUserAccessAsync(db, user.UserKey, false, "admin-key", DateTimeOffset.UtcNow);
        await service.UpdateUserRolesAsync(db, user.UserKey, [ArkanumRole.User, ArkanumRole.Auditor, ArkanumRole.VaultManager], "admin-key", DateTimeOffset.UtcNow);
        var saved = await db.Users.SingleAsync(u => u.UserKey == user.UserKey);

        Assert.False(saved.AppAccessEnabled);
        Assert.Contains("Auditor", saved.RolesCsv);
        Assert.Contains("VaultManager", saved.RolesCsv);
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == Arkanum.Web.Domain.AuditEventType.UserDisabled && e.Target == "role-target");
        Assert.Contains(await db.AuditEvents.ToListAsync(), e => e.Type == Arkanum.Web.Domain.AuditEventType.RoleChanged && e.Target == "role-target");
    }

    [Fact]
    public void Settings_page_uses_real_tabs_and_contains_admin_sections()
    {
        var root = ProjectRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "js", "settings-tabs.js"));
        var css = File.ReadAllText(Path.Combine(root, "Arkanum.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("role=\"tablist\"", markup);
        Assert.Contains("data-settings-tab=\"settings-dashboard\"", markup);
        Assert.Contains("settings-directory", markup);
        Assert.Contains("settings-notifications", markup);
        Assert.Contains("settings-users", markup);
        Assert.Contains("settings-logs", markup);
        Assert.DoesNotContain("settings-visibility", markup);
        Assert.DoesNotContain("data-settings-tab=\"settings-audit\"", markup);
        Assert.Contains("asp-page-handler=\"DashboardText\"", markup);
        Assert.Contains("asp-page-handler=\"Ldap\"", markup);
        Assert.Contains("asp-page-handler=\"Mail\"", markup);
        Assert.Contains("asp-page-handler=\"NotificationTemplates\"", markup);
        Assert.Contains("asp-page-handler=\"Syslog\"", markup);
        Assert.Contains("Bind DN password", markup);
        Assert.Contains("{{vaultName}}", markup);
        Assert.Contains("asp-page-handler=\"SendAllUsersNotification\"", markup);
        Assert.Contains("Send notification to all active users", markup);
        Assert.Contains("Vault object read", markup);
        Assert.Contains("User permissions changes", markup);
        Assert.Contains("Create local user", markup);
        Assert.Contains("Search users", markup);
        Assert.Contains("asp-page-handler=\"ToggleUserAccess\"", markup);
        Assert.Contains("asp-page-handler=\"UpdateUserRoles\"", markup);
        Assert.Contains("Directory users", markup);
        Assert.Contains("Local users", markup);
        Assert.Contains("Groups", markup);
        Assert.Contains("Delegated manager", markup);
        Assert.Contains("action=\"/Settings#users\"", markup);
        Assert.Contains("id=\"settings-user-search\"", markup);
        Assert.Contains("data-user-row", markup);
        Assert.Contains("userSearch.addEventListener('input'", script);
        Assert.Contains("panel.hidden", script);
        Assert.Contains(".settings-tab-button.active", css);
    }

    [Fact]
    public void Dashboard_uses_editable_text_and_no_step_number_badges()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Index.cshtml"));

        Assert.Contains("@Model.DashboardText.HeroTitle", markup);
        Assert.Contains("@Model.DashboardText.HeroDescription", markup);
        Assert.DoesNotContain("Step 1", markup);
        Assert.DoesNotContain("Step 2", markup);
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
