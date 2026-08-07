namespace Arkanum.Web.Services;

public enum AuditForwardingPath
{
    UdpSyslog,
    TlsSyslog,
    WindowsEventLog,
    ApprovedSiemForwarder,
    Unsupported
}

public sealed record AuditForwardingFailure(DateTimeOffset OccurredAt, string Protocol, string ErrorType);

public sealed class AuditForwardingStatus
{
    private readonly object gate = new();
    private AuditForwardingFailure? lastFailure;

    public AuditForwardingFailure? LastFailure
    {
        get
        {
            lock (gate)
            {
                return lastFailure;
            }
        }
    }

    public void RecordFailure(string auditEventId, string protocol, Exception exception)
    {
        lock (gate)
        {
            lastFailure = new AuditForwardingFailure(DateTimeOffset.UtcNow, NormalizeProtocol(protocol), exception.GetType().Name);
        }
    }

    public void RecordUnsupported(string protocol)
    {
        lock (gate)
        {
            lastFailure = new AuditForwardingFailure(DateTimeOffset.UtcNow, NormalizeProtocol(protocol), "UnsupportedForwardingPath");
        }
    }

    public void RecordSuccess()
    {
        lock (gate)
        {
            lastFailure = null;
        }
    }

    private static string NormalizeProtocol(string protocol) => string.IsNullOrWhiteSpace(protocol) ? "unspecified" : protocol.Trim();
}
