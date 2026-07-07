using Arkanum.Web.Data;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => ArkanumCookiePolicy.Configure(options, builder.Configuration))
    .AddOpenIdConnect(FederatedAuthenticationService.OidcScheme, _ => { });
builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ArkanumOidcOptionsConfigurator>();
builder.Services.AddAuthorization();
builder.Services.AddScoped<FirstRunSetupService>();
builder.Services.AddScoped<LocalPasswordHasher>();
builder.Services.AddScoped<LocalAuthenticationService>();
builder.Services.AddScoped<VaultRepository>();
builder.Services.AddScoped<VaultAuthorizationService>();
builder.Services.AddScoped<AdminUserService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PortalSettingsService>();
builder.Services.AddScoped<FederatedAuthenticationService>();
builder.Services.AddScoped<DirectoryLdapImportService>();
builder.Services.AddHostedService<DirectorySyncScheduler>();
builder.Services.AddSingleton<IMasterKeyProvider, EnvironmentMasterKeyProvider>();
builder.Services.AddScoped<SiteRecoveryKeyService>();
builder.Services.AddScoped<SecretEncryptionService>();
builder.Services.AddScoped<VaultItemService>();
builder.Services.AddScoped<VaultExportService>();
builder.Services.AddScoped<CredentialResetReminderService>();
builder.Services.AddSingleton<IEmailSender, NoOpEmailSender>();
builder.Services.AddSingleton<StrongPasswordGenerator>();
builder.Services.AddSingleton(new AuditRetentionPolicy(TimeSpan.FromDays(90)));
builder.Services.AddScoped<SyslogAuditForwardingInterceptor>();
builder.Services.AddScoped<SyslogAuditForwarder>();
builder.Services.AddScoped<ArkanumHealthCheckService>();
builder.Services.AddScoped<InstallReportService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var sqliteConnectionString = builder.Configuration.GetConnectionString("SqliteFallback") ?? "Data Source=/srv/blackbox/hermes/projects/arkanum/data/arkanum-dev.db";
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<ArkanumDbContext>((sp, options) => options
        .UseSqlServer(connectionString)
        .AddInterceptors(sp.GetRequiredService<SyslogAuditForwardingInterceptor>()));
}
else if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ArkanumDbContext>((sp, options) => options
        .UseInMemoryDatabase("ArkanumTesting")
        .AddInterceptors(sp.GetRequiredService<SyslogAuditForwardingInterceptor>()));
}
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(sqliteConnectionString.Replace("Data Source=", string.Empty)) ?? ".");
    builder.Services.AddDbContext<ArkanumDbContext>((sp, options) => options
        .UseSqlite(sqliteConnectionString)
        .AddInterceptors(sp.GetRequiredService<SyslogAuditForwardingInterceptor>()));
}

var app = builder.Build();

app.UseArkanumSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
    db.Database.EnsureCreated();
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        db.Database.ExecuteSqlRaw("IF COL_LENGTH('Users', 'FailedLoginCount') IS NULL ALTER TABLE [Users] ADD [FailedLoginCount] int NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT 0");
        db.Database.ExecuteSqlRaw("IF COL_LENGTH('Users', 'LockoutEndAt') IS NULL ALTER TABLE [Users] ADD [LockoutEndAt] datetimeoffset NULL");
        db.Database.ExecuteSqlRaw("IF OBJECT_ID('RecoveryStates', 'U') IS NULL CREATE TABLE [RecoveryStates] ([Id] nvarchar(64) NOT NULL CONSTRAINT PK_RecoveryStates PRIMARY KEY, [RecoveryKeyId] nvarchar(128) NOT NULL, [EncryptedSentinel] nvarchar(max) NOT NULL, [EncryptionAlgorithm] nvarchar(64) NOT NULL, [CreatedAt] datetimeoffset NOT NULL)");
        db.Database.ExecuteSqlRaw("IF COL_LENGTH('VaultItems', 'ResetNotificationDate') IS NULL ALTER TABLE [VaultItems] ADD [ResetNotificationDate] date NULL");
        db.Database.ExecuteSqlRaw("IF COL_LENGTH('VaultItems', 'ResetNotificationSentAt') IS NULL ALTER TABLE [VaultItems] ADD [ResetNotificationSentAt] datetimeoffset NULL");
    }
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS DashboardWidgets (WidgetKey TEXT NOT NULL CONSTRAINT PK_DashboardWidgets PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NOT NULL, Enabled INTEGER NOT NULL, SortOrder INTEGER NOT NULL, RolesCsv TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS PortalSettings (SettingKey TEXT NOT NULL CONSTRAINT PK_PortalSettings PRIMARY KEY, Value TEXT NOT NULL, UpdatedAt TEXT NOT NULL, UpdatedByUserKey TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS RecoveryStates (Id TEXT NOT NULL CONSTRAINT PK_RecoveryStates PRIMARY KEY, RecoveryKeyId TEXT NOT NULL, EncryptedSentinel TEXT NOT NULL, EncryptionAlgorithm TEXT NOT NULL, CreatedAt TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS VaultMembers (VaultId TEXT NOT NULL, UserKey TEXT NOT NULL, CanInvite INTEGER NOT NULL, AddedByUserKey TEXT NOT NULL, AddedAt TEXT NOT NULL, CONSTRAINT PK_VaultMembers PRIMARY KEY (VaultId, UserKey))");
        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_VaultMembers_UserKey ON VaultMembers (UserKey)");
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS UserGroups (Id TEXT NOT NULL CONSTRAINT PK_UserGroups PRIMARY KEY, Name TEXT NOT NULL, Description TEXT NOT NULL, CreatedAt TEXT NOT NULL)");
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS UserGroupMembers (GroupId TEXT NOT NULL, UserKey TEXT NOT NULL, AddedByUserKey TEXT NOT NULL, AddedAt TEXT NOT NULL, CONSTRAINT PK_UserGroupMembers PRIMARY KEY (GroupId, UserKey))");
        db.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS UserGroupManagers (GroupId TEXT NOT NULL, UserKey TEXT NOT NULL, AddedByUserKey TEXT NOT NULL, AddedAt TEXT NOT NULL, CONSTRAINT PK_UserGroupManagers PRIMARY KEY (GroupId, UserKey))");
        TrySqlitePatch(db, "ALTER TABLE Users ADD COLUMN FailedLoginCount INTEGER NOT NULL DEFAULT 0");
        TrySqlitePatch(db, "ALTER TABLE Users ADD COLUMN LockoutEndAt TEXT NULL");
        TrySqlitePatch(db, "ALTER TABLE VaultItems ADD COLUMN ResetNotificationDate TEXT NULL");
        TrySqlitePatch(db, "ALTER TABLE VaultItems ADD COLUMN ResetNotificationSentAt TEXT NULL");
    }
    scope.ServiceProvider.GetRequiredService<DashboardService>().EnsureDefaultWidgetsAsync(db).GetAwaiter().GetResult();
    scope.ServiceProvider.GetRequiredService<PortalSettingsService>().EnsureDefaultsAsync(db).GetAwaiter().GetResult();
    try
    {
        scope.ServiceProvider.GetRequiredService<SiteRecoveryKeyService>().EnsureRecoverySentinelAsync(db, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
    }
    catch
    {
        // First-run setup can generate a local site recovery key when no environment key has been provided yet.
    }
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async (ArkanumDbContext db, ArkanumHealthCheckService health, CancellationToken cancellationToken) =>
{
    var response = await health.CheckAsync(db, cancellationToken);
    return response.IsHealthy ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.MapGet("/install-report", async (ArkanumDbContext db, InstallReportService reports, CancellationToken cancellationToken) =>
{
    var report = await reports.BuildAsync(db, cancellationToken);
    return Results.Ok(report);
});
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.Run();

static void TrySqlitePatch(ArkanumDbContext db, string sql)
{
    try
    {
        db.Database.ExecuteSqlRaw(sql);
    }
    catch (Exception ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
    {
        // Existing SQLite review databases may already have this prototype column.
    }
}

public partial class Program { }
