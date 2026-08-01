using Arkanum.Web.Data;
using Arkanum.Web.Domain;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace Arkanum.Web.Services;

public static class PortalSettingKeys
{
    public const string DashboardHeroTitle = "dashboard.hero.title";
    public const string DashboardHeroDescription = "dashboard.hero.description";
    public const string LdapHost = "ldap.host";
    public const string LdapPort = "ldap.port";
    public const string LdapBaseDn = "ldap.baseDn";
    public const string LdapBindDn = "ldap.bindDn";
    public const string LdapBindPassword = "ldap.bindPassword";
    public const string LdapUserFilter = "ldap.userFilter";
    public const string LdapLoginAttribute = "ldap.loginAttribute";
    public const string LdapDisplayNameAttribute = "ldap.displayNameAttribute";
    public const string LdapEmailAttribute = "ldap.emailAttribute";
    public const string LdapSecurityMode = "ldap.securityMode";
    public const string LdapVerifyServerCertificate = "ldap.verifyServerCertificate";
    public const string LdapAllowInsecureForDevelopment = "ldap.allowInsecureForDevelopment";
    public const string LdapSyncInterval = "ldap.syncInterval";
    public const string MailSmtpHost = "mail.smtpHost";
    public const string MailSmtpPort = "mail.smtpPort";
    public const string MailUseTls = "mail.useTls";
    public const string MailUsername = "mail.username";
    public const string MailPassword = "mail.password";
    public const string MailFromAddress = "mail.fromAddress";
    public const string MailFromName = "mail.fromName";
    public const string NotificationAllUsersSubject = "notifications.allUsers.subject";
    public const string NotificationAllUsersBody = "notifications.allUsers.body";
    public const string NotificationVaultAccessSubject = "notifications.vaultAccess.subject";
    public const string NotificationVaultAccessBody = "notifications.vaultAccess.body";
    public const string CredentialResetReminderEnabled = "notifications.credentialReset.enabled";
    public const string CredentialResetReminderSubject = "notifications.credentialReset.subject";
    public const string CredentialResetReminderBody = "notifications.credentialReset.body";
    public const string CredentialResetReminderDailySendTime = "notifications.credentialReset.dailySendTime";
    public const string SyslogHost = "syslog.host";
    public const string SyslogPort = "syslog.port";
    public const string SyslogProtocol = "syslog.protocol";
    public const string SyslogEnabled = "syslog.enabled";
    public const string SqlServerHost = "database.sqlServer.host";
    public const string SqlServerPort = "database.sqlServer.port";
    public const string SqlServerDatabase = "database.sqlServer.database";
    public const string SqlServerUser = "database.sqlServer.user";
    public const string SqlServerPassword = "database.sqlServer.password";
    public const string SqlServerEncrypt = "database.sqlServer.encrypt";
    public const string SqlServerTrustServerCertificate = "database.sqlServer.trustServerCertificate";
    public const string FederationMode = "federation.mode";
    public const string OidcAuthority = "federation.oidc.authority";
    public const string OidcClientId = "federation.oidc.clientId";
    public const string OidcClientSecret = "federation.oidc.clientSecret";
    public const string OidcScopes = "federation.oidc.scopes";
    public const string SamlMetadataUrl = "federation.saml.metadataUrl";
    public const string SamlEntityId = "federation.saml.entityId";
    public const string RequireExternalMfa = "federation.requireExternalMfa";
}

public sealed record DashboardTextSettings(string HeroTitle, string HeroDescription);
public sealed record LdapSettings(string Host, string Port, string BaseDn, string BindDn, string BindPassword, string UserFilter, string LoginAttribute, string DisplayNameAttribute, string EmailAttribute, bool HasBindPassword = false, string SecurityMode = "LDAPS", bool VerifyServerCertificate = true, bool AllowInsecureForDevelopment = false);
public sealed record LdapSyncSettings(string Interval);
public sealed record MailServerSettings(string SmtpHost, string SmtpPort, bool UseTls, string FromAddress, string FromName, string Username = "", bool HasPassword = false);
public sealed record NotificationTemplateSettings(string AllUsersSubject, string AllUsersBody, string VaultAccessSubject, string VaultAccessBody);
public sealed record CredentialResetReminderSettings(bool Enabled, string SubjectTemplate, string BodyTemplate, string DailySendTime);
public sealed record SyslogSettings(string Host, string Port, string Protocol, bool Enabled);
public sealed record SqlServerSettings(string Host, string Port, string Database, string User, bool HasPassword, bool Encrypt, bool TrustServerCertificate);
public sealed record FederationSettings(string Mode, string OidcAuthority, string OidcClientId, bool HasOidcClientSecret, string OidcScopes, string SamlMetadataUrl, string SamlEntityId, bool RequireExternalMfa)
{
    public bool OidcConfigured => string.Equals(Mode, "OIDC", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(OidcAuthority) && !string.IsNullOrWhiteSpace(OidcClientId);
    public bool SamlConfigured => string.Equals(Mode, "SAML", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(SamlMetadataUrl) && !string.IsNullOrWhiteSpace(SamlEntityId);
    public bool ExternalIdentityConfigured => OidcConfigured || SamlConfigured;
}

public sealed class PortalSettingsService(SecretEncryptionService secretEncryption)
{
    public static readonly string[] ValidLdapSyncIntervals = ["30m", "12h", "1d"];

    private const string ProtectedSettingPrefix = "arkanum-secret:v1:";

    private static readonly HashSet<string> SecretSettingKeys =
    [
        PortalSettingKeys.LdapBindPassword,
        PortalSettingKeys.MailPassword,
        PortalSettingKeys.SqlServerPassword,
        PortalSettingKeys.OidcClientSecret
    ];

    private readonly SecretEncryptionService secretEncryption = secretEncryption;

    private static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [PortalSettingKeys.DashboardHeroTitle] = "Your starting point for vault work.",
        [PortalSettingKeys.DashboardHeroDescription] = "Open the vaults available to you and keep shared credentials organized without turning the landing page into an administration console.",
        [PortalSettingKeys.LdapHost] = string.Empty,
        [PortalSettingKeys.LdapPort] = "636",
        [PortalSettingKeys.LdapBaseDn] = string.Empty,
        [PortalSettingKeys.LdapBindDn] = string.Empty,
        [PortalSettingKeys.LdapBindPassword] = string.Empty,
        [PortalSettingKeys.LdapUserFilter] = "(objectClass=person)",
        [PortalSettingKeys.LdapLoginAttribute] = "uid",
        [PortalSettingKeys.LdapDisplayNameAttribute] = "displayName",
        [PortalSettingKeys.LdapEmailAttribute] = "mail",
        [PortalSettingKeys.LdapSecurityMode] = "LDAPS",
        [PortalSettingKeys.LdapVerifyServerCertificate] = "true",
        [PortalSettingKeys.LdapAllowInsecureForDevelopment] = "false",
        [PortalSettingKeys.LdapSyncInterval] = "12h",
        [PortalSettingKeys.MailSmtpHost] = string.Empty,
        [PortalSettingKeys.MailSmtpPort] = "587",
        [PortalSettingKeys.MailUseTls] = "true",
        [PortalSettingKeys.MailUsername] = string.Empty,
        [PortalSettingKeys.MailPassword] = string.Empty,
        [PortalSettingKeys.MailFromAddress] = string.Empty,
        [PortalSettingKeys.MailFromName] = "Arkanum",
        [PortalSettingKeys.NotificationAllUsersSubject] = "Arkanum notice",
        [PortalSettingKeys.NotificationAllUsersBody] = "Hello {{displayName}},\n\nThis is an application notice from Arkanum.",
        [PortalSettingKeys.NotificationVaultAccessSubject] = "Vault access granted: {{vaultName}}",
        [PortalSettingKeys.NotificationVaultAccessBody] = "Hello {{displayName}},\n\nYou have been granted access to the {{vaultName}} vault.",
        [PortalSettingKeys.CredentialResetReminderEnabled] = "true",
        [PortalSettingKeys.CredentialResetReminderSubject] = "Arkanum credential reset reminder",
        [PortalSettingKeys.CredentialResetReminderBody] = "Hello {{displayName}},\n\nThe following credentials are due for review or password reset:\n\n{{CredentialList}}\n\nSign in to Arkanum to review the credential. This email does not contain secret values.",
        [PortalSettingKeys.CredentialResetReminderDailySendTime] = "08:00",
        [PortalSettingKeys.SyslogHost] = string.Empty,
        [PortalSettingKeys.SyslogPort] = "514",
        [PortalSettingKeys.SyslogProtocol] = "UDP",
        [PortalSettingKeys.SyslogEnabled] = "false",
        [PortalSettingKeys.SqlServerHost] = string.Empty,
        [PortalSettingKeys.SqlServerPort] = "1433",
        [PortalSettingKeys.SqlServerDatabase] = "Arkanum",
        [PortalSettingKeys.SqlServerUser] = string.Empty,
        [PortalSettingKeys.SqlServerPassword] = string.Empty,
        [PortalSettingKeys.SqlServerEncrypt] = "true",
        [PortalSettingKeys.SqlServerTrustServerCertificate] = "false",
        [PortalSettingKeys.FederationMode] = "Local",
        [PortalSettingKeys.OidcAuthority] = string.Empty,
        [PortalSettingKeys.OidcClientId] = string.Empty,
        [PortalSettingKeys.OidcClientSecret] = string.Empty,
        [PortalSettingKeys.OidcScopes] = "openid profile email",
        [PortalSettingKeys.SamlMetadataUrl] = string.Empty,
        [PortalSettingKeys.SamlEntityId] = string.Empty,
        [PortalSettingKeys.RequireExternalMfa] = "true"
    };

    public async Task EnsureDefaultsAsync(ArkanumDbContext db)
    {
        foreach (var (key, value) in Defaults)
        {
            if (!await db.PortalSettings.AnyAsync(s => s.SettingKey == key))
            {
                db.PortalSettings.Add(new PortalSettingEntity { SettingKey = key, Value = value, UpdatedAt = DateTimeOffset.UtcNow, UpdatedByUserKey = "system" });
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(ArkanumDbContext db)
    {
        await EnsureDefaultsAsync(db);
        await ProtectExistingPlaintextSecretSettingsAsync(db);
        var values = await db.PortalSettings.AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => UnprotectIfSecret(s.SettingKey, s.Value));
        return Defaults.ToDictionary(kvp => kvp.Key, kvp => values.GetValueOrDefault(kvp.Key, kvp.Value));
    }

    public async Task<DashboardTextSettings> GetDashboardTextAsync(ArkanumDbContext db)
    {
        var settings = await GetAllAsync(db);
        return new DashboardTextSettings(settings[PortalSettingKeys.DashboardHeroTitle], settings[PortalSettingKeys.DashboardHeroDescription]);
    }

    public async Task SaveDashboardTextAsync(ArkanumDbContext db, string title, string description, string actorUserKey)
    {
        await SetAsync(db, new Dictionary<string, string>
        {
            [PortalSettingKeys.DashboardHeroTitle] = string.IsNullOrWhiteSpace(title) ? Defaults[PortalSettingKeys.DashboardHeroTitle] : title.Trim(),
            [PortalSettingKeys.DashboardHeroDescription] = string.IsNullOrWhiteSpace(description) ? Defaults[PortalSettingKeys.DashboardHeroDescription] : description.Trim()
        }, actorUserKey);
    }

    public async Task SaveLdapAsync(ArkanumDbContext db, LdapSettings input, string actorUserKey)
    {
        var values = new Dictionary<string, string>
        {
            [PortalSettingKeys.LdapHost] = input.Host.Trim(),
            [PortalSettingKeys.LdapPort] = input.Port.Trim(),
            [PortalSettingKeys.LdapBaseDn] = input.BaseDn.Trim(),
            [PortalSettingKeys.LdapBindDn] = input.BindDn.Trim(),
            [PortalSettingKeys.LdapUserFilter] = input.UserFilter.Trim(),
            [PortalSettingKeys.LdapLoginAttribute] = input.LoginAttribute.Trim(),
            [PortalSettingKeys.LdapDisplayNameAttribute] = input.DisplayNameAttribute.Trim(),
            [PortalSettingKeys.LdapEmailAttribute] = input.EmailAttribute.Trim(),
            [PortalSettingKeys.LdapSecurityMode] = NormalizeLdapSecurityMode(input.SecurityMode),
            [PortalSettingKeys.LdapVerifyServerCertificate] = input.VerifyServerCertificate ? "true" : "false",
            [PortalSettingKeys.LdapAllowInsecureForDevelopment] = input.AllowInsecureForDevelopment ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(input.BindPassword)) values[PortalSettingKeys.LdapBindPassword] = input.BindPassword.Trim();
        await SetAsync(db, values, actorUserKey);
    }

    public async Task SaveLdapSyncAsync(ArkanumDbContext db, LdapSyncSettings input, string actorUserKey)
    {
        var interval = ValidLdapSyncIntervals.Contains(input.Interval) ? input.Interval : "12h";
        await SetAsync(db, new Dictionary<string, string> { [PortalSettingKeys.LdapSyncInterval] = interval }, actorUserKey);
    }

    public async Task SaveMailAsync(ArkanumDbContext db, MailServerSettings input, string actorUserKey) => await SaveMailAsync(db, input, null, actorUserKey);

    public async Task SaveMailAsync(ArkanumDbContext db, MailServerSettings input, string? password, string actorUserKey)
    {
        var values = new Dictionary<string, string>
        {
            [PortalSettingKeys.MailSmtpHost] = input.SmtpHost.Trim(),
            [PortalSettingKeys.MailSmtpPort] = input.SmtpPort.Trim(),
            [PortalSettingKeys.MailUseTls] = input.UseTls ? "true" : "false",
            [PortalSettingKeys.MailUsername] = input.Username.Trim(),
            [PortalSettingKeys.MailFromAddress] = input.FromAddress.Trim(),
            [PortalSettingKeys.MailFromName] = input.FromName.Trim()
        };
        if (!string.IsNullOrWhiteSpace(password)) values[PortalSettingKeys.MailPassword] = password.Trim();
        await SetAsync(db, values, actorUserKey);
    }

    public async Task SaveNotificationTemplatesAsync(ArkanumDbContext db, NotificationTemplateSettings input, string actorUserKey) => await SetAsync(db, new Dictionary<string, string>
    {
        [PortalSettingKeys.NotificationAllUsersSubject] = input.AllUsersSubject.Trim(),
        [PortalSettingKeys.NotificationAllUsersBody] = input.AllUsersBody.Trim(),
        [PortalSettingKeys.NotificationVaultAccessSubject] = input.VaultAccessSubject.Trim(),
        [PortalSettingKeys.NotificationVaultAccessBody] = input.VaultAccessBody.Trim()
    }, actorUserKey);

    public async Task SaveCredentialResetRemindersAsync(ArkanumDbContext db, CredentialResetReminderSettings input, string actorUserKey) => await SetAsync(db, new Dictionary<string, string>
    {
        [PortalSettingKeys.CredentialResetReminderEnabled] = input.Enabled ? "true" : "false",
        [PortalSettingKeys.CredentialResetReminderSubject] = string.IsNullOrWhiteSpace(input.SubjectTemplate) ? Defaults[PortalSettingKeys.CredentialResetReminderSubject] : input.SubjectTemplate.Trim(),
        [PortalSettingKeys.CredentialResetReminderBody] = string.IsNullOrWhiteSpace(input.BodyTemplate) ? Defaults[PortalSettingKeys.CredentialResetReminderBody] : input.BodyTemplate.Trim(),
        [PortalSettingKeys.CredentialResetReminderDailySendTime] = NormalizeDailySendTime(input.DailySendTime)
    }, actorUserKey);

    public async Task SaveSyslogAsync(ArkanumDbContext db, SyslogSettings input, string actorUserKey) => await SetAsync(db, new Dictionary<string, string>
    {
        [PortalSettingKeys.SyslogHost] = input.Host.Trim(),
        [PortalSettingKeys.SyslogPort] = input.Port.Trim(),
        [PortalSettingKeys.SyslogProtocol] = input.Protocol.Trim(),
        [PortalSettingKeys.SyslogEnabled] = input.Enabled ? "true" : "false"
    }, actorUserKey);

    public async Task SaveSqlServerAsync(ArkanumDbContext db, string host, string port, string database, string user, string? password, bool encrypt, bool trustServerCertificate, string actorUserKey)
    {
        var values = new Dictionary<string, string>
        {
            [PortalSettingKeys.SqlServerHost] = host.Trim(),
            [PortalSettingKeys.SqlServerPort] = string.IsNullOrWhiteSpace(port) ? Defaults[PortalSettingKeys.SqlServerPort] : port.Trim(),
            [PortalSettingKeys.SqlServerDatabase] = string.IsNullOrWhiteSpace(database) ? Defaults[PortalSettingKeys.SqlServerDatabase] : database.Trim(),
            [PortalSettingKeys.SqlServerUser] = user.Trim(),
            [PortalSettingKeys.SqlServerEncrypt] = encrypt ? "true" : "false",
            [PortalSettingKeys.SqlServerTrustServerCertificate] = trustServerCertificate ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(password))
        {
            values[PortalSettingKeys.SqlServerPassword] = password.Trim();
        }
        await SetAsync(db, values, actorUserKey);
    }

    public async Task SaveFederationAsync(ArkanumDbContext db, string mode, string oidcAuthority, string oidcClientId, string? oidcClientSecret, string oidcScopes, string samlMetadataUrl, string samlEntityId, bool requireExternalMfa, string actorUserKey)
    {
        var normalizedMode = NormalizeFederationMode(mode);
        var values = new Dictionary<string, string>
        {
            [PortalSettingKeys.FederationMode] = normalizedMode,
            [PortalSettingKeys.OidcAuthority] = oidcAuthority.Trim(),
            [PortalSettingKeys.OidcClientId] = oidcClientId.Trim(),
            [PortalSettingKeys.OidcScopes] = string.IsNullOrWhiteSpace(oidcScopes) ? Defaults[PortalSettingKeys.OidcScopes] : oidcScopes.Trim(),
            [PortalSettingKeys.SamlMetadataUrl] = samlMetadataUrl.Trim(),
            [PortalSettingKeys.SamlEntityId] = samlEntityId.Trim(),
            [PortalSettingKeys.RequireExternalMfa] = requireExternalMfa ? "true" : "false"
        };
        if (!string.IsNullOrWhiteSpace(oidcClientSecret))
        {
            values[PortalSettingKeys.OidcClientSecret] = oidcClientSecret.Trim();
        }
        await SetAsync(db, values, actorUserKey);
    }

    public static string NormalizeFederationMode(string mode) => mode.Trim().ToUpperInvariant() switch
    {
        "OIDC" => "OIDC",
        "SAML" => "SAML",
        _ => "Local"
    };

    public static LdapSettings ToLdap(IReadOnlyDictionary<string, string> settings) => new(settings[PortalSettingKeys.LdapHost], settings[PortalSettingKeys.LdapPort], settings[PortalSettingKeys.LdapBaseDn], settings[PortalSettingKeys.LdapBindDn], settings[PortalSettingKeys.LdapBindPassword], settings[PortalSettingKeys.LdapUserFilter], settings[PortalSettingKeys.LdapLoginAttribute], settings[PortalSettingKeys.LdapDisplayNameAttribute], settings[PortalSettingKeys.LdapEmailAttribute], !string.IsNullOrWhiteSpace(settings[PortalSettingKeys.LdapBindPassword]), NormalizeLdapSecurityMode(settings[PortalSettingKeys.LdapSecurityMode]), string.Equals(settings[PortalSettingKeys.LdapVerifyServerCertificate], "true", StringComparison.OrdinalIgnoreCase), string.Equals(settings[PortalSettingKeys.LdapAllowInsecureForDevelopment], "true", StringComparison.OrdinalIgnoreCase));
    public static LdapSyncSettings ToLdapSync(IReadOnlyDictionary<string, string> settings) => new(settings[PortalSettingKeys.LdapSyncInterval]);
    public static MailServerSettings ToMail(IReadOnlyDictionary<string, string> settings) => new(settings[PortalSettingKeys.MailSmtpHost], settings[PortalSettingKeys.MailSmtpPort], string.Equals(settings[PortalSettingKeys.MailUseTls], "true", StringComparison.OrdinalIgnoreCase), settings[PortalSettingKeys.MailFromAddress], settings[PortalSettingKeys.MailFromName], settings[PortalSettingKeys.MailUsername], !string.IsNullOrWhiteSpace(settings[PortalSettingKeys.MailPassword]));
    public static NotificationTemplateSettings ToNotificationTemplates(IReadOnlyDictionary<string, string> settings) => new(settings[PortalSettingKeys.NotificationAllUsersSubject], settings[PortalSettingKeys.NotificationAllUsersBody], settings[PortalSettingKeys.NotificationVaultAccessSubject], settings[PortalSettingKeys.NotificationVaultAccessBody]);
    public static CredentialResetReminderSettings ToCredentialResetReminders(IReadOnlyDictionary<string, string> settings) => new(string.Equals(settings[PortalSettingKeys.CredentialResetReminderEnabled], "true", StringComparison.OrdinalIgnoreCase), settings[PortalSettingKeys.CredentialResetReminderSubject], settings[PortalSettingKeys.CredentialResetReminderBody], settings[PortalSettingKeys.CredentialResetReminderDailySendTime]);
    public static SyslogSettings ToSyslog(IReadOnlyDictionary<string, string> settings) => new(settings[PortalSettingKeys.SyslogHost], settings[PortalSettingKeys.SyslogPort], settings[PortalSettingKeys.SyslogProtocol], string.Equals(settings[PortalSettingKeys.SyslogEnabled], "true", StringComparison.OrdinalIgnoreCase));
    public static SqlServerSettings ToSqlServer(IReadOnlyDictionary<string, string> settings) => new(settings[PortalSettingKeys.SqlServerHost], settings[PortalSettingKeys.SqlServerPort], settings[PortalSettingKeys.SqlServerDatabase], settings[PortalSettingKeys.SqlServerUser], !string.IsNullOrWhiteSpace(settings[PortalSettingKeys.SqlServerPassword]), string.Equals(settings[PortalSettingKeys.SqlServerEncrypt], "true", StringComparison.OrdinalIgnoreCase), string.Equals(settings[PortalSettingKeys.SqlServerTrustServerCertificate], "true", StringComparison.OrdinalIgnoreCase));
    public static FederationSettings ToFederation(IReadOnlyDictionary<string, string> settings) => new(
        settings[PortalSettingKeys.FederationMode],
        settings[PortalSettingKeys.OidcAuthority],
        settings[PortalSettingKeys.OidcClientId],
        !string.IsNullOrWhiteSpace(settings[PortalSettingKeys.OidcClientSecret]),
        settings[PortalSettingKeys.OidcScopes],
        settings[PortalSettingKeys.SamlMetadataUrl],
        settings[PortalSettingKeys.SamlEntityId],
        string.Equals(settings[PortalSettingKeys.RequireExternalMfa], "true", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeDailySendTime(string value) => TimeOnly.TryParse(value, out var parsed) ? parsed.ToString("HH:mm") : Defaults[PortalSettingKeys.CredentialResetReminderDailySendTime];

    private static string NormalizeLdapSecurityMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "STARTTLS" => "StartTLS",
        "INSECURE" => "Insecure",
        _ => "LDAPS"
    };

    private async Task SetAsync(ArkanumDbContext db, IReadOnlyDictionary<string, string> values, string actorUserKey)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, value) in values)
        {
            var storedValue = ProtectIfSecret(key, value);
            var setting = await db.PortalSettings.SingleOrDefaultAsync(s => s.SettingKey == key);
            if (setting is null)
            {
                db.PortalSettings.Add(new PortalSettingEntity { SettingKey = key, Value = storedValue, UpdatedAt = now, UpdatedByUserKey = actorUserKey });
            }
            else
            {
                setting.Value = storedValue;
                setting.UpdatedAt = now;
                setting.UpdatedByUserKey = actorUserKey;
            }
        }
        db.AuditEvents.Add(new AuditEventEntity
        {
            Type = AuditEventType.SettingsChanged,
            ActorUserKey = actorUserKey,
            Target = "Portal settings",
            OccurredAt = now,
            Message = $"Settings updated: {string.Join(", ", values.Keys.OrderBy(k => k))}. Values were not logged."
        });
        await db.SaveChangesAsync();
    }

    private async Task ProtectExistingPlaintextSecretSettingsAsync(ArkanumDbContext db)
    {
        var existingSecrets = await db.PortalSettings
            .Where(s => SecretSettingKeys.Contains(s.SettingKey) && s.Value != string.Empty)
            .ToListAsync();
        var changed = false;
        foreach (var setting in existingSecrets.Where(s => !TryUnprotectSecret(s.Value, out _)))
        {
            setting.Value = ProtectIfSecret(setting.SettingKey, setting.Value);
            setting.UpdatedAt = DateTimeOffset.UtcNow;
            setting.UpdatedByUserKey = string.IsNullOrWhiteSpace(setting.UpdatedByUserKey) ? "system" : setting.UpdatedByUserKey;
            changed = true;
        }
        if (changed)
        {
            await db.SaveChangesAsync();
        }
    }

    private string ProtectIfSecret(string key, string value)
    {
        if (!SecretSettingKeys.Contains(key) || string.IsNullOrWhiteSpace(value)) return value;
        if (TryUnprotectSecret(value, out _)) return value;
        var payload = secretEncryption.Encrypt(value);
        return ProtectedSettingPrefix + JsonSerializer.Serialize(payload);
    }

    private string UnprotectIfSecret(string key, string value)
    {
        if (!SecretSettingKeys.Contains(key) || string.IsNullOrWhiteSpace(value)) return value;
        if (!IsProtectedSecret(value)) return value;
        return TryUnprotectSecret(value, out var plaintext) ? plaintext : value;
    }

    private static bool IsProtectedSecret(string value) => value.StartsWith(ProtectedSettingPrefix, StringComparison.Ordinal);

    private bool TryUnprotectSecret(string value, out string plaintext)
    {
        plaintext = string.Empty;
        if (!IsProtectedSecret(value)) return false;
        try
        {
            var payloadJson = value[ProtectedSettingPrefix.Length..];
            var payload = JsonSerializer.Deserialize<EncryptedSecretPayload>(payloadJson);
            if (payload is null || !string.Equals(payload.Algorithm, "AES-256-GCM", StringComparison.Ordinal)) return false;
            plaintext = secretEncryption.Decrypt(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
