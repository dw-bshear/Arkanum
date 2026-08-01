using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using System.DirectoryServices.Protocols;
using System.Net;

namespace Arkanum.Web.Services;

public sealed record DirectoryImportResult(int Imported, int Updated, int DisabledMissing);

public sealed class DirectoryLdapImportService(IWebHostEnvironment? environment = null)
{
    private readonly string environmentName = environment?.EnvironmentName ?? "Production";

    public async Task<DirectoryImportResult> ImportAsync(ArkanumDbContext db, LdapSettings settings, string actorUserKey, DateTimeOffset now)
    {
        var records = SearchDirectory(settings);
        var externalIds = records.Select(r => r.ExternalId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var updated = 0;

        foreach (var record in records)
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == record.ExternalId && u.Source != UserSource.Local);
            if (user is null)
            {
                user = new AppUserEntity
                {
                    ExternalId = record.ExternalId,
                    Username = record.Username,
                    DisplayName = record.DisplayName,
                    Email = record.Email,
                    Source = UserSource.Directory,
                    DirectoryPresent = true,
                    AppAccessEnabled = false,
                    DisabledReason = "Pending administrator activation",
                    RolesCsv = ArkanumRole.User.ToString(),
                    CreatedAt = now
                };
                db.Users.Add(user);
                imported++;
            }
            else
            {
                user.Username = record.Username;
                user.DisplayName = record.DisplayName;
                user.Email = record.Email;
                user.DirectoryPresent = true;
                if (string.IsNullOrWhiteSpace(user.RolesCsv)) user.RolesCsv = ArkanumRole.User.ToString();
                updated++;
            }
        }

        var missingUsers = await db.Users.Where(u => u.Source != UserSource.Local && u.ExternalId != null && !externalIds.Contains(u.ExternalId)).ToListAsync();
        foreach (var user in missingUsers)
        {
            user.DirectoryPresent = false;
            user.AppAccessEnabled = false;
            user.DisabledReason = "Not found in latest directory sync";
        }

        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.DirectorySync,
            ActorUserKey = actorUserKey,
            Target = "Directory / LDAP",
            OccurredAt = now,
            Message = $"Directory sync completed. Imported {imported}, updated {updated}, disabled {missingUsers.Count}."
        });
        await db.SaveChangesAsync();
        return new DirectoryImportResult(imported, updated, missingUsers.Count);
    }

    private IReadOnlyList<DirectoryUserRecord> SearchDirectory(LdapSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Host)) throw new InvalidOperationException("LDAP host is required before running directory sync.");
        if (string.IsNullOrWhiteSpace(settings.BaseDn)) throw new InvalidOperationException("LDAP search base DN is required before running directory sync.");
        if (!int.TryParse(settings.Port, out var port)) port = string.Equals(settings.SecurityMode, "LDAPS", StringComparison.OrdinalIgnoreCase) ? 636 : 389;
        if (string.Equals(settings.SecurityMode, "Insecure", StringComparison.OrdinalIgnoreCase) && (!settings.AllowInsecureForDevelopment || !IsDevelopmentOrTesting()))
        {
            throw new InvalidOperationException("Insecure LDAP requires an explicit development/testing acknowledgment and is blocked outside Development/Testing.");
        }

        using var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, port))
        {
            AuthType = AuthType.Basic,
            Credential = new NetworkCredential(settings.BindDn, settings.BindPassword)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = string.Equals(settings.SecurityMode, "LDAPS", StringComparison.OrdinalIgnoreCase);
        if (!settings.VerifyServerCertificate)
        {
            if (!settings.AllowInsecureForDevelopment || !IsDevelopmentOrTesting())
            {
                throw new InvalidOperationException("Disabling LDAP server certificate validation is blocked outside explicit Development/Testing mode.");
            }
            connection.SessionOptions.VerifyServerCertificate = (_, _) => true;
        }
        if (string.Equals(settings.SecurityMode, "StartTLS", StringComparison.OrdinalIgnoreCase))
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }
        connection.Bind();

        var attrs = new[] { settings.LoginAttribute, settings.DisplayNameAttribute, settings.EmailAttribute, "entryUUID", "uid" }
            .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var request = new SearchRequest(settings.BaseDn, string.IsNullOrWhiteSpace(settings.UserFilter) ? "(objectClass=person)" : settings.UserFilter, SearchScope.Subtree, attrs);
        var response = (SearchResponse)connection.SendRequest(request);
        var users = new List<DirectoryUserRecord>();
        foreach (SearchResultEntry entry in response.Entries)
        {
            var username = Attr(entry, settings.LoginAttribute) ?? Attr(entry, "uid") ?? entry.DistinguishedName;
            var displayName = Attr(entry, settings.DisplayNameAttribute) ?? username;
            var email = Attr(entry, settings.EmailAttribute) ?? string.Empty;
            var externalId = Attr(entry, "entryUUID") ?? entry.DistinguishedName;
            users.Add(new DirectoryUserRecord(externalId, username, displayName, email));
        }
        return users;
    }

    private bool IsDevelopmentOrTesting() => string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase);

    private static string? Attr(SearchResultEntry entry, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !entry.Attributes.Contains(name)) return null;
        var values = entry.Attributes[name];
        return values.Count == 0 ? null : values[0]?.ToString();
    }
}
