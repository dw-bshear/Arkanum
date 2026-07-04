using System.Net.Sockets;
using System.Text;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arkanum.Web.Services;

public sealed class SyslogAuditForwarder(ILogger<SyslogAuditForwarder> logger)
{
    public async Task ForwardAsync(SyslogSettings settings, AuditEventEntity auditEvent, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host)) return;
        if (!int.TryParse(settings.Port, out var port) || port <= 0) port = 514;
        if (!string.Equals(settings.Protocol, "UDP", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Syslog forwarding skipped because protocol {Protocol} is not supported yet.", settings.Protocol);
            return;
        }

        try
        {
            var message = FormatMessage(auditEvent);
            using var client = new UdpClient();
            var bytes = Encoding.UTF8.GetBytes(message);
            await client.SendAsync(bytes, settings.Host, port, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Syslog forwarding failed for audit event {AuditEventId}. Portal operation will continue.", auditEvent.Id);
        }
    }

    public static string FormatMessage(AuditEventEntity auditEvent)
    {
        var timestamp = auditEvent.OccurredAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var safeMessage = Sanitize(auditEvent.Message);
        var safeTarget = Sanitize(auditEvent.Target);
        return $"<134>1 {timestamp} arkanum Arkanum.Web - {auditEvent.Id} type=\"{auditEvent.Type}\" actor=\"{Sanitize(auditEvent.ActorUserKey)}\" target=\"{safeTarget}\" message=\"{safeMessage}\"";
    }

    private static string Sanitize(string value) => value.Replace("\r", " ").Replace("\n", " ").Replace("\"", "'");
}

public sealed class SyslogAuditForwardingInterceptor(SyslogAuditForwarder forwarder, ILogger<SyslogAuditForwardingInterceptor> logger) : SaveChangesInterceptor
{
    private readonly HashSet<string> _forwarded = [];

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await ForwardAddedAuditEventsAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private async Task ForwardAddedAuditEventsAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is not ArkanumDbContext db) return;
        var events = db.ChangeTracker.Entries<AuditEventEntity>()
            .Where(entry => entry.State == EntityState.Unchanged)
            .Select(entry => entry.Entity)
            .Where(evt => !_forwarded.Contains(evt.Id) && evt.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-5))
            .ToArray();
        if (events.Length == 0) return;

        SyslogSettings settings;
        try
        {
            var values = await db.PortalSettings.AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => s.Value, cancellationToken);
            settings = PortalSettingsService.ToSyslog(new Dictionary<string, string>
            {
                [PortalSettingKeys.SyslogHost] = values.GetValueOrDefault(PortalSettingKeys.SyslogHost, string.Empty),
                [PortalSettingKeys.SyslogPort] = values.GetValueOrDefault(PortalSettingKeys.SyslogPort, "514"),
                [PortalSettingKeys.SyslogProtocol] = values.GetValueOrDefault(PortalSettingKeys.SyslogProtocol, "UDP"),
                [PortalSettingKeys.SyslogEnabled] = values.GetValueOrDefault(PortalSettingKeys.SyslogEnabled, "false")
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load syslog settings for audit forwarding.");
            return;
        }

        foreach (var auditEvent in events)
        {
            await forwarder.ForwardAsync(settings, auditEvent, cancellationToken);
            _forwarded.Add(auditEvent.Id);
        }
    }
}
