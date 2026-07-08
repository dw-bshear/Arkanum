using System.Runtime.InteropServices;
using Arkanum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record InstallReport(
    string Product,
    string Version,
    DateTimeOffset GeneratedAt,
    string EnvironmentName,
    string FrameworkDescription,
    string OperatingSystem,
    string DatabaseProvider,
    string HealthStatus,
    IReadOnlyList<ArkanumHealthCheck> HealthChecks,
    InstallReportCounts Counts,
    InstallReportSecurityControls SecurityControls,
    InstallReportOperationalSettings OperationalSettings,
    InstallReportFederationReadiness FederationReadiness,
    IReadOnlyList<ComplianceControlMapping> ComplianceMappings);

public sealed record InstallReportCounts(int Users, int ActiveUsers, int Vaults, int VaultItems, int AuditEvents);

public sealed record InstallReportSecurityControls(
    bool BrowserSecurityHeadersEnabled,
    bool SensitivePageNoStoreEnabled,
    bool AuthCookieHttpOnly,
    string AuthCookieSameSite,
    string AuthCookieSecurePolicy,
    int LocalAccountLockoutThreshold,
    int LocalAccountLockoutMinutes,
    bool SyslogForwardingConfigured);

public sealed record InstallReportOperationalSettings(
    bool LdapConfigured,
    bool MailConfigured,
    bool SyslogEnabled,
    bool SqlServerSettingsConfigured,
    string AuditRetention);

public sealed record InstallReportFederationReadiness(
    string Mode,
    bool OidcConfigured,
    bool SamlConfigured,
    bool ExternalMfaExpected,
    bool SmartCardDirectIntegrationRequired,
    string Notes);

public sealed class InstallReportService(
    ArkanumHealthCheckService healthCheckService,
    AuditRetentionPolicy auditRetentionPolicy,
    IWebHostEnvironment environment,
    IConfiguration configuration)
{
    public async Task<InstallReport> BuildAsync(ArkanumDbContext db, CancellationToken cancellationToken = default)
    {
        var health = await healthCheckService.CheckAsync(db, cancellationToken);
        var settings = await db.PortalSettings.AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => s.Value, cancellationToken);
        var cookieSecurePolicy = ArkanumCookiePolicy.ResolveSecurePolicy(configuration["Conclave:CookieSecurePolicy"]);
        var federation = PortalSettingsService.ToFederation(settings);

        return new InstallReport(
            Product: "Conclave",
            Version: ArkanumHealthCheckService.CurrentSchemaVersion,
            GeneratedAt: DateTimeOffset.UtcNow,
            EnvironmentName: environment.EnvironmentName,
            FrameworkDescription: RuntimeInformation.FrameworkDescription,
            OperatingSystem: RuntimeInformation.OSDescription,
            DatabaseProvider: db.Database.ProviderName ?? "unknown",
            HealthStatus: health.Status,
            HealthChecks: health.Checks,
            Counts: new InstallReportCounts(
                Users: await db.Users.CountAsync(cancellationToken),
                ActiveUsers: await db.Users.CountAsync(u => u.AppAccessEnabled, cancellationToken),
                Vaults: await db.Vaults.CountAsync(cancellationToken),
                VaultItems: await db.VaultItems.CountAsync(cancellationToken),
                AuditEvents: await db.AuditEvents.CountAsync(cancellationToken)),
            SecurityControls: new InstallReportSecurityControls(
                BrowserSecurityHeadersEnabled: true,
                SensitivePageNoStoreEnabled: true,
                AuthCookieHttpOnly: true,
                AuthCookieSameSite: "Strict",
                AuthCookieSecurePolicy: cookieSecurePolicy.ToString(),
                LocalAccountLockoutThreshold: LocalAuthenticationService.LockoutThreshold,
                LocalAccountLockoutMinutes: (int)LocalAuthenticationService.LockoutDuration.TotalMinutes,
                SyslogForwardingConfigured: IsEnabled(settings, PortalSettingKeys.SyslogEnabled) && !string.IsNullOrWhiteSpace(settings.GetValueOrDefault(PortalSettingKeys.SyslogHost))),
            OperationalSettings: new InstallReportOperationalSettings(
                LdapConfigured: !string.IsNullOrWhiteSpace(settings.GetValueOrDefault(PortalSettingKeys.LdapHost)),
                MailConfigured: !string.IsNullOrWhiteSpace(settings.GetValueOrDefault(PortalSettingKeys.MailSmtpHost)),
                SyslogEnabled: IsEnabled(settings, PortalSettingKeys.SyslogEnabled),
                SqlServerSettingsConfigured: !string.IsNullOrWhiteSpace(settings.GetValueOrDefault(PortalSettingKeys.SqlServerHost)),
                AuditRetention: $"{auditRetentionPolicy.Retention.TotalDays:0} days"),
            FederationReadiness: new InstallReportFederationReadiness(
                Mode: federation.Mode,
                OidcConfigured: federation.OidcConfigured,
                SamlConfigured: federation.SamlConfigured,
                ExternalMfaExpected: federation.RequireExternalMfa,
                SmartCardDirectIntegrationRequired: false,
                Notes: "Phase 4 favors SAML/OIDC through an enterprise IdP. Smart card/PIV can be satisfied upstream by the IdP and is optional for direct app integration."),
            ComplianceMappings: ComplianceControlCatalog.Controls);
    }

    private static bool IsEnabled(IReadOnlyDictionary<string, string> settings, string key)
    {
        return bool.TryParse(settings.GetValueOrDefault(key), out var enabled) && enabled;
    }
}
