using System.Net;
using System.Net.Mail;
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

public sealed class SmtpEmailSender(IServiceScopeFactory scopeFactory) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArkanumDbContext>();
        var settingsService = scope.ServiceProvider.GetRequiredService<PortalSettingsService>();
        var settings = await settingsService.GetAllAsync(db);
        var mail = PortalSettingsService.ToMail(settings);
        if (string.IsNullOrWhiteSpace(mail.SmtpHost) || string.IsNullOrWhiteSpace(mail.FromAddress)) return;

        using var smtp = new SmtpClient(mail.SmtpHost, int.TryParse(mail.SmtpPort, out var port) ? port : 587)
        {
            EnableSsl = mail.UseTls
        };
        var password = settings[PortalSettingKeys.MailPassword];
        if (!string.IsNullOrWhiteSpace(mail.Username) && !string.IsNullOrWhiteSpace(password))
        {
            smtp.Credentials = new NetworkCredential(mail.Username, password);
        }
        using var mailMessage = new MailMessage(new MailAddress(mail.FromAddress, mail.FromName), new MailAddress(message.To))
        {
            Subject = message.Subject,
            Body = message.Body
        };
        await smtp.SendMailAsync(mailMessage, cancellationToken);
    }
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
        var reminderSettings = PortalSettingsService.ToCredentialResetReminders(settings);
        if (!reminderSettings.Enabled) return new CredentialResetReminderResult(dueItems.Count, 0);

        var subjectTemplate = reminderSettings.SubjectTemplate;
        var bodyTemplate = reminderSettings.BodyTemplate;
        var messagesSent = 0;
        var dueVaultIds = dueItems.Select(d => d.Item.VaultId).Distinct().ToArray();
        var memberships = (await db.VaultMembers.AsNoTracking()
            .Where(m => dueVaultIds.Contains(m.VaultId))
            .Join(db.Users.AsNoTracking(), m => m.UserKey, u => u.UserKey, (m, u) => new { m.VaultId, User = u })
            .Where(x => x.User.AppAccessEnabled && x.User.Email != "")
            .ToListAsync(cancellationToken))
            .Where(x => !HasRole(x.User, ArkanumRole.Administrator))
            .ToList();

        foreach (var recipientGroup in memberships.GroupBy(x => x.User.Email.Trim().ToLowerInvariant()))
        {
            var recipient = recipientGroup.First().User;
            var allowedVaultIds = recipientGroup.Select(x => x.VaultId).ToHashSet();
            var recipientItems = dueItems.Where(d => allowedVaultIds.Contains(d.Item.VaultId)).ToList();
            if (recipientItems.Count == 0) continue;
            var subject = ApplyTemplate(subjectTemplate, recipient, recipientItems);
            var body = ApplyTemplate(bodyTemplate, recipient, recipientItems);
            await emailSender.SendAsync(new EmailMessage(recipient.Email.Trim(), subject, body), cancellationToken);
            messagesSent++;
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

    private static bool HasRole(AppUserEntity user, ArkanumRole role) => user.RolesCsv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(r => string.Equals(r, role.ToString(), StringComparison.OrdinalIgnoreCase));

    private static string ApplyTemplate(string template, AppUserEntity recipient, IReadOnlyList<DueCredential> dueItems)
    {
        var lines = dueItems.Select(d => $"Vault: {d.VaultName}\nCredential: {d.Item.Title}\nDue date: {d.Item.ResetNotificationDate:yyyy-MM-dd}");
        return template
            .Replace("{{displayName}}", string.IsNullOrWhiteSpace(recipient.DisplayName) ? recipient.Username : recipient.DisplayName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{email}}", recipient.Email, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CredentialList}}", string.Join("\n\n", lines), StringComparison.OrdinalIgnoreCase)
            .Replace("{{VaultName}}", dueItems[0].VaultName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CredentialName}}", dueItems[0].Item.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{{DueDate}}", dueItems[0].Item.ResetNotificationDate?.ToString("yyyy-MM-dd") ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record DueCredential(VaultItemEntity Item, string VaultName);
}
