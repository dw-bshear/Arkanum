using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arkanum.Web.Services;

public sealed class SyslogAuditForwarder(ILogger<SyslogAuditForwarder> logger, AuditForwardingStatus forwardingStatus)
{
    public async Task ForwardAsync(SyslogSettings settings, AuditEventEntity auditEvent, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host)) return;
        if (!int.TryParse(settings.Port, out var port) || port <= 0) port = 514;
        var path = ResolveForwardingPath(settings);
        if (path == AuditForwardingPath.Unsupported)
        {
            forwardingStatus.RecordUnsupported(settings.Protocol);
            logger.LogWarning("Audit forwarding skipped because protocol {Protocol} is not an approved forwarding path.", settings.Protocol);
            return;
        }

        try
        {
            var message = FormatMessage(auditEvent);
            switch (path)
            {
                case AuditForwardingPath.UdpSyslog:
                    using (var client = new UdpClient())
                    {
                        var bytes = Encoding.UTF8.GetBytes(message);
                        await client.SendAsync(bytes, settings.Host, port, cancellationToken);
                    }
                    break;
                case AuditForwardingPath.TlsSyslog:
                    await SendTlsSyslogAsync(settings.Host, port == 514 ? 6514 : port, message, cancellationToken);
                    break;
                case AuditForwardingPath.WindowsEventLog:
                    logger.LogInformation("Audit event {AuditEventId} is delegated to the approved Windows Event Log forwarding path.", auditEvent.Id);
                    break;
                case AuditForwardingPath.ApprovedSiemForwarder:
                    logger.LogInformation("Audit event {AuditEventId} is delegated to the locally approved SIEM forwarder path.", auditEvent.Id);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported audit forwarding path {path}.");
            }
            forwardingStatus.RecordSuccess();
        }
        catch (Exception ex)
        {
            forwardingStatus.RecordFailure(auditEvent.Id, settings.Protocol, ex);
            logger.LogWarning(ex, "Audit forwarding failed for audit event {AuditEventId}. Portal operation will continue and health will report the forwarding failure.", auditEvent.Id);
        }
    }

    public static AuditForwardingPath ResolveForwardingPath(SyslogSettings settings)
    {
        var protocol = settings.Protocol.Trim();
        if (string.Equals(protocol, "UDP", StringComparison.OrdinalIgnoreCase)) return AuditForwardingPath.UdpSyslog;
        if (string.Equals(protocol, "TLS", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "TCP+TLS", StringComparison.OrdinalIgnoreCase)) return AuditForwardingPath.TlsSyslog;
        if (string.Equals(protocol, "WindowsEventLog", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "Windows Event Log", StringComparison.OrdinalIgnoreCase)) return AuditForwardingPath.WindowsEventLog;
        if (string.Equals(protocol, "SIEMForwarder", StringComparison.OrdinalIgnoreCase) || string.Equals(protocol, "Approved SIEM Forwarder", StringComparison.OrdinalIgnoreCase)) return AuditForwardingPath.ApprovedSiemForwarder;
        return AuditForwardingPath.Unsupported;
    }

    public static string FormatMessage(AuditEventEntity auditEvent)
    {
        var timestamp = auditEvent.OccurredAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var safeMessage = Sanitize(auditEvent.Message);
        var safeTarget = Sanitize(auditEvent.Target);
        return $"<134>1 {timestamp} arkanum Arkanum.Web - {auditEvent.Id} type=\"{auditEvent.Type}\" actor=\"{Sanitize(auditEvent.ActorUserKey)}\" target=\"{safeTarget}\" message=\"{safeMessage}\" sequence=\"{auditEvent.Sequence}\" hash=\"{auditEvent.IntegrityHash}\" previousHash=\"{auditEvent.PreviousIntegrityHash}\"";
    }

    private static async Task SendTlsSyslogAsync(string host, int port, string message, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await stream.AuthenticateAsClientAsync(host);
        var payload = Encoding.UTF8.GetBytes(message + "\n");
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
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
