using Arkanum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record ArkanumHealthResponse(string Status, DateTimeOffset CheckedAt, string Version, IReadOnlyList<ArkanumHealthCheck> Checks)
{
    public bool IsHealthy => string.Equals(Status, "Healthy", StringComparison.OrdinalIgnoreCase);
}

public sealed record ArkanumHealthCheck(string Name, string Status, string Message);

public sealed class ArkanumHealthCheckService(IMasterKeyProvider masterKeyProvider, AuditForwardingStatus? auditForwardingStatus = null)
{
    public const string CurrentSchemaVersion = "phase4-preview";
    private static readonly string[] RequiredSettingKeys =
    [
        PortalSettingKeys.DashboardHeroTitle,
        PortalSettingKeys.LdapHost,
        PortalSettingKeys.MailSmtpHost,
        PortalSettingKeys.SyslogEnabled,
        PortalSettingKeys.SqlServerHost
    ];

    public async Task<ArkanumHealthResponse> CheckAsync(ArkanumDbContext db, CancellationToken cancellationToken = default)
    {
        var checks = new List<ArkanumHealthCheck>
        {
            new("application", "Healthy", "Arkanum application process is responding.")
        };

        await CheckDatabaseAsync(db, checks, cancellationToken);
        await CheckSettingsAsync(db, checks, cancellationToken);
        CheckEncryptionKey(checks);
        await CheckRecoveryKeyAsync(db, checks, cancellationToken);
        CheckAuditForwarding(checks);

        var status = checks.Any(check => check.Status == "Unhealthy") ? "Unhealthy" : "Healthy";
        return new ArkanumHealthResponse(status, DateTimeOffset.UtcNow, CurrentSchemaVersion, checks);
    }

    private static async Task CheckDatabaseAsync(ArkanumDbContext db, ICollection<ArkanumHealthCheck> checks, CancellationToken cancellationToken)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                checks.Add(new ArkanumHealthCheck("database", "Unhealthy", "Database connection could not be opened."));
                return;
            }

            await db.Users.Take(1).CountAsync(cancellationToken);
            await db.Vaults.Take(1).CountAsync(cancellationToken);
            await db.VaultItems.Take(1).CountAsync(cancellationToken);
            await db.PortalSettings.Take(1).CountAsync(cancellationToken);
            checks.Add(new ArkanumHealthCheck("database", "Healthy", $"Database is reachable using provider {db.Database.ProviderName ?? "unknown"}."));
        }
        catch (Exception ex)
        {
            checks.Add(new ArkanumHealthCheck("database", "Unhealthy", $"Database health check failed: {ex.GetType().Name}."));
        }
    }

    private static async Task CheckSettingsAsync(ArkanumDbContext db, ICollection<ArkanumHealthCheck> checks, CancellationToken cancellationToken)
    {
        try
        {
            var present = await db.PortalSettings
                .Where(setting => RequiredSettingKeys.Contains(setting.SettingKey))
                .Select(setting => setting.SettingKey)
                .ToListAsync(cancellationToken);
            var missing = RequiredSettingKeys.Except(present).ToArray();
            if (missing.Length == 0)
            {
                checks.Add(new ArkanumHealthCheck("settings", "Healthy", "Required portal defaults are present."));
            }
            else
            {
                checks.Add(new ArkanumHealthCheck("settings", "Unhealthy", $"Missing required portal setting defaults: {string.Join(", ", missing)}."));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new ArkanumHealthCheck("settings", "Unhealthy", $"Settings health check failed: {ex.GetType().Name}."));
        }
    }

    private void CheckEncryptionKey(ICollection<ArkanumHealthCheck> checks)
    {
        try
        {
            var key = masterKeyProvider.GetMasterKey();
            if (key.Length < 32)
            {
                checks.Add(new ArkanumHealthCheck("encryption-key", "Unhealthy", "Configured master encryption key is shorter than the required key length."));
                return;
            }

            checks.Add(new ArkanumHealthCheck("encryption-key", "Healthy", $"Master encryption key is available through key id {masterKeyProvider.KeyId}."));
        }
        catch (Exception ex)
        {
            checks.Add(new ArkanumHealthCheck("encryption-key", "Unhealthy", $"Master encryption key check failed: {ex.GetType().Name}."));
        }
    }

    private async Task CheckRecoveryKeyAsync(ArkanumDbContext db, ICollection<ArkanumHealthCheck> checks, CancellationToken cancellationToken)
    {
        try
        {
            var result = await new SiteRecoveryKeyService(masterKeyProvider).ValidateRecoverySentinelAsync(db, cancellationToken);
            checks.Add(new ArkanumHealthCheck("recovery-key", result.IsValid ? "Healthy" : "Unhealthy", result.Message));
        }
        catch (Exception ex)
        {
            checks.Add(new ArkanumHealthCheck("recovery-key", "Unhealthy", $"Site recovery key validation failed: {ex.GetType().Name}."));
        }
    }

    private void CheckAuditForwarding(ICollection<ArkanumHealthCheck> checks)
    {
        var lastFailure = auditForwardingStatus?.LastFailure;
        if (lastFailure is null)
        {
            checks.Add(new ArkanumHealthCheck("audit-forwarding", "Healthy", "No audit forwarding failures have been recorded for this application process."));
            return;
        }

        checks.Add(new ArkanumHealthCheck(
            "audit-forwarding",
            "Unhealthy",
            $"Last audit forwarding failure used {lastFailure.Protocol} and failed with {lastFailure.ErrorType}. Audit event identifiers and message contents are intentionally omitted."));
    }
}
