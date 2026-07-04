using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Arkanum.Tests;

public class Iteration010DirectorySyncSettingsTests
{
    [Fact]
    public async Task Ldap_sync_interval_setting_is_persisted()
    {
        await using var db = CreateDb();
        var service = new PortalSettingsService();

        await service.SaveLdapSyncAsync(db, new LdapSyncSettings("12h"), "admin-key");
        var settings = await service.GetAllAsync(db);

        Assert.Equal("12h", settings[PortalSettingKeys.LdapSyncInterval]);
        Assert.Equal("30m", PortalSettingsService.ValidLdapSyncIntervals[0]);
        Assert.Equal("12h", PortalSettingsService.ValidLdapSyncIntervals[1]);
        Assert.Equal("1d", PortalSettingsService.ValidLdapSyncIntervals[2]);
        Assert.Equal(TimeSpan.FromMinutes(30), DirectorySyncScheduler.IntervalToTimeSpan("30m"));
        Assert.Equal(TimeSpan.FromHours(12), DirectorySyncScheduler.IntervalToTimeSpan("12h"));
        Assert.Equal(TimeSpan.FromDays(1), DirectorySyncScheduler.IntervalToTimeSpan("1d"));
    }

    [Fact]
    public void Directory_settings_show_manual_sync_button_and_interval_choices()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var pageModel = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));
        var program = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Program.cs"));

        Assert.Contains("asp-page-handler=\"RunDirectorySync\"", markup);
        Assert.Contains("Sync directory now", markup);
        Assert.Contains("Directory sync schedule", markup);
        Assert.Contains("value=\"30m\"", markup);
        Assert.Contains("Every 30 minutes", markup);
        Assert.Contains("value=\"12h\"", markup);
        Assert.Contains("Every 12 hours", markup);
        Assert.Contains("value=\"1d\"", markup);
        Assert.Contains("Every 1 day", markup);
        Assert.Contains("asp-page-handler=\"LdapSyncSchedule\"", markup);
        Assert.Contains("OnPostRunDirectorySyncAsync", pageModel);
        Assert.Contains("OnPostLdapSyncScheduleAsync", pageModel);
        Assert.Contains("AddHostedService<DirectorySyncScheduler>", program);
    }

    [Fact]
    public async Task Sql_server_connection_settings_are_persisted_without_requiring_password_reentry()
    {
        await using var db = CreateDb();
        var service = new PortalSettingsService();

        await service.SaveSqlServerAsync(db, "sql.example.local", "1433", "ArkanumReview", "arkanum_app", "initial-password", true, true, "admin-key");
        await service.SaveSqlServerAsync(db, "sql2.example.local", "11433", "ArkanumProd", "arkanum_runtime", string.Empty, false, true, "admin-key");
        var settings = await service.GetAllAsync(db);
        var sql = PortalSettingsService.ToSqlServer(settings);

        Assert.Equal("sql2.example.local", sql.Host);
        Assert.Equal("11433", sql.Port);
        Assert.Equal("ArkanumProd", sql.Database);
        Assert.Equal("arkanum_runtime", sql.User);
        Assert.True(sql.HasPassword);
        Assert.False(sql.Encrypt);
        Assert.True(sql.TrustServerCertificate);
        Assert.Equal("initial-password", settings[PortalSettingKeys.SqlServerPassword]);
    }

    [Fact]
    public void Settings_page_exposes_admin_database_tab_for_sql_server_options()
    {
        var markup = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml"));
        var pageModel = File.ReadAllText(Path.Combine(ProjectRoot(), "Arkanum.Web", "Pages", "Settings", "Index.cshtml.cs"));

        Assert.Contains("settings-database", markup);
        Assert.Contains("SQL Server connection settings", markup);
        Assert.Contains("asp-page-handler=\"SqlServer\"", markup);
        Assert.Contains("name=\"SqlServerHost\"", markup);
        Assert.Contains("name=\"SqlServerDatabase\"", markup);
        Assert.Contains("name=\"SqlServerUser\"", markup);
        Assert.Contains("Password already saved; leave blank to keep it", markup);
        Assert.Contains("Initial installation still needs a separate setup workflow", markup);
        Assert.Contains("OnPostSqlServerAsync", pageModel);
        Assert.Contains("ActiveDatabaseProvider", pageModel);
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
