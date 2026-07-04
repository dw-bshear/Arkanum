using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class AdminUserService(LocalPasswordHasher passwordHasher)
{
    public async Task<ArkanumUser> CreateLocalUserAsync(ArkanumDbContext db, string username, string password, IReadOnlyCollection<ArkanumRole> roles, string actorUserKey, DateTimeOffset now)
    {
        var normalized = username.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ArgumentException("Username is required.", nameof(username));
        if (await db.Users.AnyAsync(u => u.Username == normalized)) throw new InvalidOperationException("A user with that username already exists.");
        var selectedRoles = ArkanumRoleCatalog.Normalize(roles);

        var entity = new AppUserEntity
        {
            Username = normalized,
            DisplayName = normalized,
            Source = UserSource.Local,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            PasswordHash = passwordHasher.Hash(password),
            RolesCsv = string.Join(',', selectedRoles),
            CreatedAt = now
        };
        db.Users.Add(entity);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.UserEnabled,
            ActorUserKey = actorUserKey,
            Target = entity.Username,
            OccurredAt = now,
            Message = "Local user created and enabled by administrator."
        });
        await db.SaveChangesAsync();
        return FirstRunSetupService.ToDomain(entity);
    }

    public async Task SetUserAccessAsync(ArkanumDbContext db, string userKey, bool enabled, string actorUserKey, DateTimeOffset now)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserKey == userKey) ?? throw new InvalidOperationException("User not found.");
        if (user.AppAccessEnabled == enabled) return;
        user.AppAccessEnabled = enabled;
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = enabled ? AuditEventType.UserEnabled : AuditEventType.UserDisabled,
            ActorUserKey = actorUserKey,
            Target = user.Username,
            OccurredAt = now,
            Message = enabled ? "Application access enabled for user." : "Application access disabled for user."
        });
        await db.SaveChangesAsync();
    }

    public async Task UpdateUserRolesAsync(ArkanumDbContext db, string userKey, IReadOnlyCollection<ArkanumRole> roles, string actorUserKey, DateTimeOffset now)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.UserKey == userKey) ?? throw new InvalidOperationException("User not found.");
        var selectedRoles = ArkanumRoleCatalog.Normalize(roles);
        var rolesCsv = string.Join(',', selectedRoles);
        if (string.Equals(user.RolesCsv, rolesCsv, StringComparison.Ordinal)) return;
        user.RolesCsv = rolesCsv;
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.RoleChanged,
            ActorUserKey = actorUserKey,
            Target = user.Username,
            OccurredAt = now,
            Message = $"Roles updated: {rolesCsv}."
        });
        await db.SaveChangesAsync();
    }
}
