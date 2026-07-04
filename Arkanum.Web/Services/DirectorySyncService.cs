using Arkanum.Web.Domain;

namespace Arkanum.Web.Services;

public sealed record DirectoryUserRecord(string ExternalId, string Username, string DisplayName, string Email);
public sealed record DirectorySyncResult(IReadOnlyList<ArkanumUser> Users, IReadOnlyList<AuditEvent> Events);

public sealed class DirectorySyncService
{
    public DirectorySyncResult SyncUsers(IEnumerable<ArkanumUser> existingUsers, IEnumerable<DirectoryUserRecord> directoryUsers, DateTimeOffset syncedAt)
    {
        var existingByExternalId = existingUsers.Where(u => !string.IsNullOrWhiteSpace(u.ExternalId)).ToDictionary(u => u.ExternalId!, StringComparer.OrdinalIgnoreCase);
        var directoryByExternalId = directoryUsers.ToDictionary(u => u.ExternalId, StringComparer.OrdinalIgnoreCase);
        var results = new List<ArkanumUser>();
        var events = new List<AuditEvent>();

        foreach (var directoryUser in directoryByExternalId.Values)
        {
            if (!existingByExternalId.TryGetValue(directoryUser.ExternalId, out var user))
            {
                user = new ArkanumUser
                {
                    ExternalId = directoryUser.ExternalId,
                    Username = directoryUser.Username,
                    DisplayName = directoryUser.DisplayName,
                    Email = directoryUser.Email,
                    Source = UserSource.Directory,
                    DirectoryPresent = true,
                    AppAccessEnabled = false,
                    DisabledReason = "Pending administrator activation",
                    LastDirectorySyncAt = syncedAt,
                    Roles = [ArkanumRole.User]
                };
                events.Add(new AuditEvent(Guid.NewGuid().ToString("n"), AuditEventType.DirectorySync, "system", syncedAt) { Target = user.Username, Message = "Imported disabled directory user pending administrator activation." });
            }
            else
            {
                var wasMissing = !user.DirectoryPresent;
                user.Username = directoryUser.Username;
                user.DisplayName = directoryUser.DisplayName;
                user.Email = directoryUser.Email;
                user.DirectoryPresent = true;
                user.LastDirectorySyncAt = syncedAt;
                if (wasMissing)
                {
                    user.AppAccessEnabled = false;
                    user.DisabledReason = "Returned after absence; requires administrator reactivation";
                }
            }
            if (!user.Roles.Contains(ArkanumRole.User)) user.Roles.Add(ArkanumRole.User);
            results.Add(user);
        }

        foreach (var user in existingByExternalId.Values.Where(u => !directoryByExternalId.ContainsKey(u.ExternalId!)))
        {
            user.DirectoryPresent = false;
            user.AppAccessEnabled = false;
            user.DisabledReason = "Not found in latest directory sync";
            user.LastDirectorySyncAt = syncedAt;
            results.Add(user);
            events.Add(new AuditEvent(Guid.NewGuid().ToString("n"), AuditEventType.UserDisabled, "system", syncedAt) { Target = user.Username, Message = "Directory user disabled because they were not found in the latest sync." });
        }

        return new DirectorySyncResult(results, events);
    }
}
