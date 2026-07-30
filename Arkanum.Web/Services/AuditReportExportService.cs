using System.Text;
using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class AuditReportExportService
{
    public async Task<VaultExportFile> ExportAuditCsvAsync(ArkanumDbContext db, string actorUserKey, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var storedEvents = await db.AuditEvents.AsNoTracking().ToListAsync(cancellationToken);
        var events = storedEvents
            .OrderByDescending(e => e.OccurredAt)
            .ThenBy(e => e.Type)
            .ThenBy(e => e.ActorUserKey)
            .ToArray();

        var fileName = $"Arkanum-AuditReport-{now:yyyyMMddHHmmss}.csv";
        var csv = ToCsv(events);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SettingsChanged,
            ActorUserKey = actorUserKey,
            Target = "Audit report",
            OccurredAt = now,
            Message = $"Audit CSV report exported. Export file: {fileName}."
        });
        await db.SaveChangesAsync(cancellationToken);

        return new VaultExportFile(fileName, "text/csv", Encoding.UTF8.GetBytes(csv));
    }

    private static string ToCsv(IReadOnlyList<AuditEventEntity> events)
    {
        var builder = new StringBuilder();
        builder.AppendLine("OccurredAtUtc,Type,ActorUserKey,Target,Message");
        foreach (var item in events)
        {
            builder.AppendLine(string.Join(',',
                Escape(item.OccurredAt.ToUniversalTime().ToString("O")),
                Escape(item.Type.ToString()),
                Escape(item.ActorUserKey),
                Escape(item.Target),
                Escape(NormalizeCell(item.Message))));
        }

        return builder.ToString();
    }

    private static string NormalizeCell(string value) => value.Replace('\r', ' ').Replace('\n', ' ');

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
