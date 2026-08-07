using Microsoft.Extensions.Configuration;
using Arkanum.Web.Domain;

namespace Arkanum.Web.Services;

public sealed class AuditRetentionPolicy(TimeSpan retention)
{
    private static readonly TimeSpan DefaultRetention = TimeSpan.FromDays(90);

    public TimeSpan Retention { get; } = retention;
    public string DisplayName => $"{Retention.TotalDays:0} days";

    public static AuditRetentionPolicy FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration["Arkanum:AuditRetentionDays"];
        if (int.TryParse(configured, out var days) && days > 0)
        {
            return new AuditRetentionPolicy(TimeSpan.FromDays(days));
        }

        return new AuditRetentionPolicy(DefaultRetention);
    }

    public IReadOnlyList<AuditEvent> FilterRetained(IEnumerable<AuditEvent> events, DateTimeOffset now)
    {
        var cutoff = now.Subtract(Retention);
        return events.Where(e => e.OccurredAt >= cutoff).OrderBy(e => e.OccurredAt).ToArray();
    }
}
