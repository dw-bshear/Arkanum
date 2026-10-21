using System.Security.Claims;
using Arkanum.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record FeedbackResult(bool Success, string Message);

// Per-account, process-wide cooldown; bounded entries and no stored feedback or uploads.
// Multi-instance installations must apply a shared gateway rate limit as well.
public sealed class FeedbackRateLimiter
{
    readonly Dictionary<string, DateTimeOffset> attempts = new(StringComparer.Ordinal);
    readonly object gate = new();
    public bool TryAcquire(string userKey)
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in attempts.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) attempts.Remove(key);
            if (attempts.ContainsKey(userKey) || attempts.Count >= 10000) return false;
            attempts[userKey] = now.AddMinutes(1);
            return true;
        }
    }
}

public sealed class FeedbackService(PortalSettingsService settings, IEmailSender sender, FeedbackRateLimiter limiter)
{
    public const int MaxScreenshotBytes = 2 * 1024 * 1024;
    public async Task<FeedbackResult> SubmitAsync(ArkanumDbContext db, ClaimsPrincipal user, string? text,
        IReadOnlyList<string> answers, IReadOnlyList<IFormFile> screenshots, CancellationToken cancellationToken)
    {
        var key = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(key)) return new(false, "Sign in to send feedback.");
        var config = await settings.GetFeedbackPublicAsync(db);
        if (!config.Enabled) return new(false, "Feedback is not available.");
        if (!limiter.TryAcquire(key)) return new(false, "Please wait one minute before sending more feedback.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000) return new(false, "Enter feedback of 1 to 1000 characters.");
        if (answers.Count > config.Questions.Count || answers.Count > 5 || answers.Any(a => a is null || a.Length > 500))
            return new(false, "Question answers must be at most 500 characters each.");
        if (screenshots.Count > 2 || screenshots.Any(f => f.Length <= 0 || f.Length > MaxScreenshotBytes) || screenshots.Sum(f => f.Length) > 2L * MaxScreenshotBytes)
            return new(false, "Attach at most two PNG or JPEG screenshots, up to 2 MiB each (4 MiB total).");
        var attachments = new List<EmailAttachment>();
        try
        {
            foreach (var file in screenshots)
            {
                // Read no more than the limit even if the declared Length is false. No filesystem staging.
                await using var input = file.OpenReadStream();
                using var output = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (output.Length + read > MaxScreenshotBytes) return new(false, "Screenshot exceeds 2 MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                var bytes = output.ToArray();
                var png = bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) && bytes.AsSpan(12,4).SequenceEqual("IHDR"u8);
                var jpeg = bytes.Length >= 4 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff && bytes[^2] == 0xff && bytes[^1] == 0xd9;
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!(png && extension == ".png" || jpeg && (extension == ".jpg" || extension == ".jpeg")))
                    return new(false, "Screenshots must contain PNG or JPEG image data and use the matching extension.");
                attachments.Add(new($"screenshot-{attachments.Count + 1}.{(png ? "png" : "jpg")}", png ? "image/png" : "image/jpeg", bytes));
            }
            // Only the server retrieves the private destination; never include it in public models/errors.
            var all = await settings.GetAllAsync(db);
            var recipient = all[PortalSettingKeys.FeedbackRecipient];
            var mail = PortalSettingsService.ToMail(all);
            if (!PortalSettingsService.IsSingleFeedbackRecipient(recipient) || string.IsNullOrWhiteSpace(mail.SmtpHost) || string.IsNullOrWhiteSpace(mail.FromAddress))
                return new(false, "Feedback could not be delivered. Please contact your administrator.");
            var body = $"Feedback from account: {key}\n\n{text.Trim()}";
            for (var i = 0; i < config.Questions.Count; i++) body += $"\n\n{config.Questions[i]}\n{(i < answers.Count ? answers[i] : "(not answered)")}";
            await sender.SendAsync(new EmailMessage(recipient, "Arkanum user feedback", body, attachments, RequireDelivery: true), cancellationToken);
            return new(true, "Feedback sent. Thank you.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return new(false, "Feedback could not be delivered. Please try again later or contact your administrator."); }
    }
}
