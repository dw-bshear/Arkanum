using Arkanum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class CredentialResetReminderHostedService(IServiceScopeFactory scopeFactory, ILogger<CredentialResetReminderHostedService> logger) : BackgroundService
{
    private DateOnly? _lastRunDate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
                var settingsService = scope.ServiceProvider.GetRequiredService<PortalSettingsService>();
                var settings = await settingsService.GetAllAsync(db);
                var reminderSettings = PortalSettingsService.ToCredentialResetReminders(settings);
                var now = DateTimeOffset.Now;
                var today = DateOnly.FromDateTime(now.Date);
                if (reminderSettings.Enabled && ShouldRun(now, reminderSettings.DailySendTime) && _lastRunDate != today)
                {
                    var service = scope.ServiceProvider.GetRequiredService<CredentialResetReminderService>();
                    var result = await service.SendDueRemindersAsync(db, now, stoppingToken);
                    _lastRunDate = today;
                    logger.LogInformation("Credential reset reminder run completed. DueItems={DueItems} MessagesSent={MessagesSent}", result.DueItems, result.MessagesSent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Credential reset reminder run failed without exposing secret values.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    internal static bool ShouldRun(DateTimeOffset now, string dailySendTime)
    {
        if (!TimeOnly.TryParse(dailySendTime, out var scheduled)) scheduled = new TimeOnly(8, 0);
        var current = TimeOnly.FromDateTime(now.LocalDateTime);
        return current >= scheduled;
    }
}
