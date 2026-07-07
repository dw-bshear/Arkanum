using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed record CredentialResetReminderResult(int DueItems, int MessagesSent);

public sealed class CredentialResetReminderService(IEmailSender emailSender, PortalSettingsService settingsService)
{
    public async Task<CredentialResetReminderResult> SendDueRemindersAsync(ArkanumDbContext db, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var dueItems = await db.VaultItems
            .Where(i => i.ResetNotificationDate != null && i.ResetNotificationDate <= today && i.ResetNotificationSentAt == null)
            .Join(db.Vaults, i => i.VaultId, v => v.Id, (i, v) => new DueCredential(i, v.Name))
            .ToListAsync(cancellationToken);
        if (dueItems.Count == 0) return new CredentialResetReminderResult(0, 0);

        var settings = await settingsService.GetAllAsync(db);
        var subjectTemplate = settings.GetValueOrDefault(PortalSettingKeys.CredentialResetReminderSubject, "Arkanum credential reset reminder");
        var bodyTemplate = settings.GetValueOrDefault(PortalSettingKeys.CredentialResetReminderBody, DefaultBodyTemplate);
        var messagesSent = 0;

        foreach (var group in dueItems.GroupBy(d => d.Item.VaultId))
        {
            var vaultItems = group.ToList();
            var recipients = await db.VaultMembers.AsNoTracking()
                .Where(m => m.VaultId == group.Key)
                .Join(db.Users.AsNoTracking(), m => m.UserKey, u => u.UserKey, (_, u) => u)
                .Where(u => u.AppAccessEnabled && u.Email != "" && !u.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(nameof(ArkanumRole.Administrator)))
                .ToListAsync(cancellationToken);

            foreach (var recipient in recipients.DistinctBy(u => u.Email.ToLowerInvariant()))
            {
                var subject = subjectTemplate.Replace("{{VaultName}}", vaultItems[0].VaultName, StringComparison.OrdinalIgnoreCase);
                var body = BuildBody(bodyTemplate, recipient, vaultItems);
                await emailSender.SendAsync(new EmailMessage(recipient.Email, subject, body), cancellationToken);
                messagesSent++;
            }
        }

        foreach (var due in dueItems)
        {
            due.Item.ResetNotificationSentAt = now;
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.SecretUpdated,
                ActorUserKey = "system",
                Target = due.Item.Title,
                OccurredAt = now,
                Message = $"Credential reset reminder sent for '{due.Item.Title}' in vault '{due.VaultName}'. Secret value was not included."
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return new CredentialResetReminderResult(dueItems.Count, messagesSent);
    }

    private static string BuildBody(string template, AppUserEntity recipient, IReadOnlyList<DueCredential> dueItems)
    {
        var lines = dueItems.Select(d => $"Vault: {d.VaultName}\nCredential: {d.Item.Title}\nDue date: {d.Item.ResetNotificationDate:yyyy-MM-dd}");
        return template
            .Replace("{{displayName}}", string.IsNullOrWhiteSpace(recipient.DisplayName) ? recipient.Username : recipient.DisplayName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CredentialList}}", string.Join("\n\n", lines), StringComparison.OrdinalIgnoreCase)
            .Replace("{{VaultName}}", dueItems[0].VaultName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CredentialName}}", dueItems[0].Item.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{{DueDate}}", dueItems[0].Item.ResetNotificationDate?.ToString("yyyy-MM-dd") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private const string DefaultBodyTemplate = "Hello {{displayName}},\n\nThe following credentials are due for review or password reset:\n\n{{CredentialList}}\n\nSign in to Arkanum to review the credential. This email does not contain secret values.";

    private sealed record DueCredential(VaultItemEntity Item, string VaultName);
}
