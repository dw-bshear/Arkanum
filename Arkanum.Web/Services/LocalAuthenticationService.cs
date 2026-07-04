using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed record AuthenticationResult(bool Succeeded, ArkanumUser? User, string Message);

public sealed class LocalAuthenticationService(LocalPasswordHasher passwordHasher)
{
    public const int LockoutThreshold = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthenticationResult> AuthenticateAsync(ArkanumDbContext db, string username, string password, DateTimeOffset now)
    {
        var normalized = username.Trim();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == normalized && u.Source == UserSource.Local);
        if (user is null || !user.AppAccessEnabled)
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.LoginFailed,
                ActorUserKey = user?.UserKey ?? "anonymous",
                Target = normalized,
                OccurredAt = now,
                Message = "Local login failed for unknown or disabled account."
            });
            await db.SaveChangesAsync();
            return new AuthenticationResult(false, null, "Invalid username or password.");
        }

        if (user.LockoutEndAt is not null && user.LockoutEndAt > now)
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = AuditEventType.LoginFailed,
                ActorUserKey = user.UserKey,
                Target = user.Username,
                OccurredAt = now,
                Message = "Local login blocked because the account is temporarily locked."
            });
            await db.SaveChangesAsync();
            return new AuthenticationResult(false, null, "Account temporarily locked. Try again later or contact an administrator.");
        }

        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            var locked = user.FailedLoginCount >= LockoutThreshold;
            if (locked)
            {
                user.LockoutEndAt = now.Add(LockoutDuration);
            }
            db.AuditEvents.Add(new AuditEventEntity
            {
                Type = locked ? AuditEventType.AccountLocked : AuditEventType.LoginFailed,
                ActorUserKey = user.UserKey,
                Target = user.Username,
                OccurredAt = now,
                Message = locked
                    ? $"Local account temporarily locked after {LockoutThreshold} failed login attempts."
                    : "Local login failed."
            });
            await db.SaveChangesAsync();
            return new AuthenticationResult(false, null, locked ? "Account temporarily locked. Try again later or contact an administrator." : "Invalid username or password.");
        }

        user.FailedLoginCount = 0;
        user.LockoutEndAt = null;
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.LoginSucceeded,
            ActorUserKey = user.UserKey,
            Target = user.Username,
            OccurredAt = now,
            Message = "Local login succeeded."
        });
        await db.SaveChangesAsync();
        return new AuthenticationResult(true, FirstRunSetupService.ToDomain(user), "Authenticated.");
    }
}
