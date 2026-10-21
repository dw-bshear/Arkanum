using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Arkanum.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// Dispatch before building the web host: setup never starts services, creates keys or seeds data.
if (Arkanum.Web.Setup.DatabaseSetupCommand.IsRequested(args))
{
    Environment.ExitCode = await Arkanum.Web.Setup.DatabaseSetupCommand.RunAsync(args, Environment.GetEnvironmentVariable, Console.Out);
    return;
}

var builder = WebApplication.CreateBuilder(args);
ApplyFileBackedRuntimeConfiguration(builder.Configuration);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/ManualLogin");
    options.Conventions.AllowAnonymousToPage("/Setup");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => ArkanumCookiePolicy.Configure(options, builder.Configuration, builder.Environment.EnvironmentName))
    .AddOpenIdConnect(FederatedAuthenticationService.OidcScheme, _ => { });
builder.Services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ArkanumOidcOptionsConfigurator>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OperationalReadiness", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(nameof(ArkanumRole.Administrator), nameof(ArkanumRole.Auditor)));
});
builder.Services.AddScoped<FirstRunSetupService>();
builder.Services.AddScoped<LocalPasswordHasher>();
builder.Services.AddScoped<LocalAuthenticationService>();
builder.Services.AddScoped<VaultRepository>();
builder.Services.AddScoped<VaultAuthorizationService>();
builder.Services.AddScoped<AdminUserService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<PortalSettingsService>();
builder.Services.AddScoped<FeedbackService>();
builder.Services.AddSingleton<FeedbackRateLimiter>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddScoped<FederatedAuthenticationService>();
builder.Services.AddScoped<DirectoryLdapImportService>();
builder.Services.AddHostedService<DirectorySyncScheduler>();
builder.Services.AddSingleton<IMasterKeyProvider, EnvironmentMasterKeyProvider>();
builder.Services.AddScoped<SiteRecoveryKeyService>();
builder.Services.AddScoped<SecretEncryptionService>();
builder.Services.AddScoped<VaultItemService>();
builder.Services.AddScoped<VaultExportService>();
builder.Services.AddScoped<CredentialResetReminderService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddHostedService<CredentialResetReminderHostedService>();
builder.Services.AddSingleton<StrongPasswordGenerator>();
builder.Services.AddSingleton(AuditRetentionPolicy.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton<AuditForwardingStatus>();
builder.Services.AddScoped<SyslogAuditForwardingInterceptor>();
builder.Services.AddScoped<SyslogAuditForwarder>();
builder.Services.AddScoped<ArkanumHealthCheckService>();
builder.Services.AddScoped<InstallReportService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var sqliteConnectionString = builder.Configuration.GetConnectionString("SqliteFallback") ?? "Data Source=arkanum-dev.db";
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
    Directory.CreateDirectory(Path.GetDirectoryName(sqliteConnectionString.Replace("Data Source=", string.Empty, StringComparison.OrdinalIgnoreCase)) ?? ".");
    builder.Services.AddDbContext<ArkanumDbContext>((sp, options) => options
        .UseSqlite(sqliteConnectionString)
        .AddInterceptors(sp.GetRequiredService<SyslogAuditForwardingInterceptor>()));
}

var app = builder.Build();

app.UseArkanumSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseExceptionHandler("/Error");
}

app.UseHttpsRedirection();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
    var providerName = db.Database.ProviderName ?? string.Empty;
    var isSqlServer = providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase);
    var allowRuntimeSchemaManagement = app.Configuration.GetValue<bool>("Arkanum:AllowRuntimeSchemaManagement");
    if (isSqlServer && allowRuntimeSchemaManagement && db.Database.IsRelational())
    {
        db.Database.Migrate();
    }
    else if (!isSqlServer)
    {
        db.Database.EnsureCreated();
        db.EnsureCredentialHistorySchemaAsync().GetAwaiter().GetResult();
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
app.MapGet("/health", () =>
{
    return Results.Ok(new ArkanumLivenessResponse("Alive", DateTimeOffset.UtcNow));
});
app.MapGet("/health/details", async (ArkanumDbContext db, ArkanumHealthCheckService health, CancellationToken cancellationToken) =>
{
    var response = await health.CheckAsync(db, cancellationToken);
    return response.IsHealthy ? Results.Ok(response) : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
}).RequireAuthorization("OperationalReadiness");
app.MapGet("/install-report", async (ArkanumDbContext db, InstallReportService reports, CancellationToken cancellationToken) =>
{
    var report = await reports.BuildAsync(db, cancellationToken);
    return Results.Ok(report);
}).RequireAuthorization("OperationalReadiness");
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets().RequireAuthorization();
app.Run();

static void ApplyFileBackedRuntimeConfiguration(IConfiguration configuration)
{
    var connectionStringFile = configuration["ConnectionStrings__DefaultConnection_FILE"] ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection_FILE");
    if (!string.IsNullOrWhiteSpace(connectionStringFile) && File.Exists(connectionStringFile))
    {
        configuration["ConnectionStrings:DefaultConnection"] = File.ReadAllText(connectionStringFile).Trim();
    }

    var masterKeyFile = configuration["ARKANUM_MASTER_KEY_FILE"] ?? Environment.GetEnvironmentVariable("ARKANUM_MASTER_KEY_FILE");
    if (!string.IsNullOrWhiteSpace(masterKeyFile) && File.Exists(masterKeyFile))
    {
        Environment.SetEnvironmentVariable("ARKANUM_MASTER_KEY", File.ReadAllText(masterKeyFile).Trim());
        configuration["Arkanum:MasterKeyFile"] = masterKeyFile;
    }
}

public partial class Program { }

public sealed record ArkanumLivenessResponse(string Status, DateTimeOffset CheckedAt);
