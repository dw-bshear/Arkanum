using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arkanum.Web.Services;

public sealed class FirstRunSetupService
{
    public bool IsSetupRequired(IEnumerable<ArkanumUser> users) =>
        !users.Any(u => u.Source == UserSource.Local && u.Roles.Contains(ArkanumRole.Administrator));

    public async Task<IReadOnlyList<ArkanumUser>> LoadUsersAsync(ArkanumDbContext db)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
        return users.Select(ToDomain).ToArray();
    }

    public async Task<ArkanumUser> CreateInitialAdministratorAsync(ArkanumDbContext db, LocalPasswordHasher passwordHasher, string username, string password, DateTimeOffset now)
    {
        var users = await LoadUsersAsync(db);
        if (!IsSetupRequired(users))
        {
            throw new InvalidOperationException("First-run setup is locked because a local administrator already exists.");
        }

        var normalized = username.Trim();
        var admin = new AppUserEntity
        {
            Username = normalized,
            DisplayName = normalized,
            Source = UserSource.Local,
            DirectoryPresent = true,
            AppAccessEnabled = true,
            PasswordHash = passwordHasher.Hash(password),
            RolesCsv = string.Join(',', ArkanumRole.Administrator, ArkanumRole.User),
            CreatedAt = now
        };
        db.Users.Add(admin);
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.FirstAdminCreated,
            ActorUserKey = admin.UserKey,
            Target = admin.Username,
            OccurredAt = now,
            Message = "Initial local administrator created from first-run setup."
        });
        await db.SaveChangesAsync();
        return ToDomain(admin);
    }

    public static ArkanumUser ToDomain(AppUserEntity u) => new()
    {
        UserKey = u.UserKey,
        ExternalId = u.ExternalId,
        Username = u.Username,
        DisplayName = u.DisplayName,
        Email = u.Email,
        Source = u.Source,
        DirectoryPresent = u.DirectoryPresent,
        AppAccessEnabled = u.AppAccessEnabled,
        DisabledReason = u.DisabledReason,
        Roles = u.RolesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<ArkanumRole>).ToList()
    };
}
