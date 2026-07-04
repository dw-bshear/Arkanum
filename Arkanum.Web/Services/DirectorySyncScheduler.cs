using Arkanum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class DirectorySyncScheduler(IServiceProvider services, ILogger<DirectorySyncScheduler> logger) : BackgroundService
{
    public static TimeSpan IntervalToTimeSpan(string interval) => interval switch
    {
        "30m" => TimeSpan.FromMinutes(30),
        "12h" => TimeSpan.FromHours(12),
        "1d" => TimeSpan.FromDays(1),
        _ => TimeSpan.FromHours(12)
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
                var settingsService = scope.ServiceProvider.GetRequiredService<PortalSettingsService>();
                var importService = scope.ServiceProvider.GetRequiredService<DirectoryLdapImportService>();
                var settings = await settingsService.GetAllAsync(db);
                var delay = IntervalToTimeSpan(settings[PortalSettingKeys.LdapSyncInterval]);
                await Task.Delay(delay, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;
                settings = await settingsService.GetAllAsync(db);
                var ldap = PortalSettingsService.ToLdap(settings);
                if (string.IsNullOrWhiteSpace(ldap.Host) || string.IsNullOrWhiteSpace(ldap.BaseDn)) continue;
                var result = await importService.ImportAsync(db, ldap, "directory-sync-scheduler", DateTimeOffset.UtcNow);
                logger.LogInformation("Directory sync completed. Imported {Imported}, updated {Updated}, disabled {Disabled}.", result.Imported, result.Updated, result.DisabledMissing);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Scheduled directory sync failed.");
                try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { break; }
            }
        }
    }
}
