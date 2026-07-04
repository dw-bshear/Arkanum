using Arkanum.Web.Domain;

namespace Arkanum.Web.Services;

public sealed class AuditRetentionPolicy(TimeSpan retention)
{
    public TimeSpan Retention { get; } = retention;

    public IReadOnlyList<AuditEvent> FilterRetained(IEnumerable<AuditEvent> events, DateTimeOffset now)
    {
        var cutoff = now.Subtract(Retention);
        return events.Where(e => e.OccurredAt >= cutoff).OrderBy(e => e.OccurredAt).ToArray();
    }
}
